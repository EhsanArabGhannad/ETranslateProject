using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ETranslate.DomainTests.Documents;

// Opt-in acceptance host: real product executables, new databases only, no AppHost or user config edits.
internal sealed class IsolatedSqlProductHost : IAsyncDisposable
{
    private readonly string server;
    private readonly List<string> ownedDatabases = [];
    private readonly List<(string Name, Process Process, StreamWriter Log)> children = [];
    private readonly Dictionary<string, string> projects = [];
    private readonly Dictionary<string, string> databases = [];
    public Dictionary<string, string> Urls { get; } = [];
    public string Root { get; }
    public string Output { get; }
    public string Storage => Path.Combine(Output, "blobs");
    public string RunId { get; } = Guid.NewGuid().ToString("N");

    public IsolatedSqlProductHost(string server, string root, string suite = "sql-pagination")
    {
        // Never accept a remote server, credentials, a caller-selected catalog or a reusable database.
        if (!Regex.IsMatch(server, @"^\.(\\[A-Za-z0-9_]+)?$")) throw new ArgumentException("Use a local SQL instance name only.");
        if (suite is not ("sql-pagination" or "editor-access" or "editor-concurrency" or "editor-recovery" or "editor-compatibility" or "editor-uploads" or "editor-performance" or "storage-failures" or "storage-inventory" or "storage-restore" or "document-journey")) throw new ArgumentException("Unknown isolated QA suite.");
        this.server = server; Root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(Root, "ETranslate.slnx"))) throw new ArgumentException("Solution root required.");
        Output = Path.Combine(Root, "tmp", "pdfs", suite, RunId);
        Directory.CreateDirectory(Output); Directory.CreateDirectory(Storage);
        foreach (var name in new[] { "identitydb", "billingdb", "workflowdb", "documentsdb", "trustdb", "messaging" })
            databases[name] = $"ETranslatePaginationQa_{RunId}_{name}";
        foreach (var name in new[] { "identity-access", "billing", "translation-workflow", "documents", "trust", "web" }) {
            string url;do{url=$"http://127.0.0.1:{FreePort()}";}while(Urls.ContainsValue(url));Urls[name]=url;
        }
    }

    public string Connection(string name) => new SqlConnectionStringBuilder
    {
        DataSource = server, InitialCatalog = databases[name], IntegratedSecurity = true,
        Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 5, Pooling = false,
        ApplicationName = "ETranslate isolated pagination QA"
    }.ConnectionString;

    public async Task StartAsync()
    {
        await CreateDatabasesAsync();
        await StartService("identity-access", "Services/IdentityAccess/ETranslate.IdentityAccess.Api");
        await StartService("billing", "Services/Billing/ETranslate.Billing.Api");
        await StartService("translation-workflow", "Services/TranslationWorkflow/ETranslate.TranslationWorkflow.Api");
        await StartService("documents", "Services/Documents/ETranslate.Documents.Api");
        await StartService("trust", "Services/Trust/ETranslate.Trust.Api");
        await StartService("web", "Web/ETranslate.Web");
    }

    public async Task CreateDatabasesAsync()
    {
        var master = new SqlConnectionStringBuilder(Connection("messaging")) { InitialCatalog = "master" };
        await using (var sql = new SqlConnection(master.ConnectionString))
        {
            await sql.OpenAsync();
            foreach (var database in databases.Values)
            {
                await using var exists = new SqlCommand("SELECT DB_ID(@name)", sql);
                exists.Parameters.AddWithValue("@name", database);
                if (await exists.ExecuteScalarAsync() is not DBNull) throw new InvalidOperationException("Refusing to reuse an existing QA database.");
                await using var create = new SqlCommand($"CREATE DATABASE [{database}]", sql) { CommandTimeout = 45 };
                await create.ExecuteNonQueryAsync(); ownedDatabases.Add(database);
            }
        }
    }

    private async Task StartService(string name, string relativeProject)
    {
        projects[name] = relativeProject;
        var project = Path.Combine(Root, "src", relativeProject);
        var dll = Path.Combine(project, "bin", "Debug", "net10.0", Path.GetFileName(project) + ".dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the solution before acceptance testing.", dll);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = project };
        info.ArgumentList.Add(dll);
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("services__", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["DOTNET_ENVIRONMENT"] = "Development";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["ASPNETCORE_URLS"] = Urls[name];
        info.Environment["Logging__LogLevel__Default"] = "Warning";
        info.Environment["Logging__LogLevel__Microsoft"] = "Warning";
        info.Environment["DocumentStorage__RootPath"] = Storage;
        info.Environment["PLAYWRIGHT_BROWSERS_PATH"] = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        foreach (var key in databases.Keys) info.Environment["ConnectionStrings__" + key] = Connection(key);
        foreach (var service in Urls) info.Environment[$"services__{service.Key}__http__0"] = service.Value;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var log = new StreamWriter(Path.Combine(Output, name + ".log"), append: true) { AutoFlush = true };
        void Write(object sender, DataReceivedEventArgs e) { if (e.Data is not null) lock (log) log.WriteLine(e.Data); }
        process.OutputDataReceived += Write; process.ErrorDataReceived += Write;
        try { process.Start(); } catch { log.Dispose(); process.Dispose(); throw; }
        children.Add((name, process, log)); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"{name} exited; see {name}.log in the QA output.");
            try { using var response = await client.GetAsync(Urls[name] + "/alive"); if (response.StatusCode == HttpStatusCode.OK) return; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
            await Task.Delay(200);
        }
        throw new TimeoutException($"{name} did not start; inspect the isolated QA log.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); }
    }

    // Rehearsal only: the source and destination are generated owned QA catalogs.
    public async Task<string> BackupAndRestoreDocumentsAsync()
    {
        var source = databases["documentsdb"];
        if (!ownedDatabases.Contains(source)) throw new InvalidOperationException("Owned source required.");
        var target = $"ETranslatePaginationQa_{RunId}_restored";
        var backup = Path.GetFullPath(Path.Combine(Output, "documents-copy-only.bak"));
        var data = Path.GetFullPath(Path.Combine(Output, "restored.mdf"));
        var log = Path.GetFullPath(Path.Combine(Output, "restored_log.ldf"));
        foreach (var path in new[] { backup, data, log })
            if (!path.StartsWith(Path.GetFullPath(Output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(path))
                throw new InvalidOperationException("Fresh owned backup/restore files required.");
        await using var sql = new SqlConnection(new SqlConnectionStringBuilder(Connection("documentsdb")) { InitialCatalog = "master" }.ConnectionString);
        await sql.OpenAsync();
        await using (var exists = new SqlCommand("SELECT DB_ID(@name)", sql)) {
            exists.Parameters.AddWithValue("@name", target);
            if (await exists.ExecuteScalarAsync() is not DBNull) throw new InvalidOperationException("Refusing to overwrite a restore target.");
        }
        await Execute($"BACKUP DATABASE [{source}] TO DISK = {Literal(backup)} WITH COPY_ONLY, CHECKSUM;");
        await Execute($"RESTORE VERIFYONLY FROM DISK = {Literal(backup)} WITH CHECKSUM;");
        var files = new List<(string Logical, string Type)>();
        await using (var list = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = {Literal(backup)}", sql))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) files.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
        if (files.Count != 2 || files.Count(x => x.Type == "D") != 1 || files.Count(x => x.Type == "L") != 1) throw new InvalidOperationException("Expected one data and one log file in the QA backup.");
        databases["restored"] = target; ownedDatabases.Add(target);
        await Execute($"RESTORE DATABASE [{target}] FROM DISK = {Literal(backup)} WITH MOVE {Literal(files.Single(x => x.Type == "D").Logical)} TO {Literal(data)}, MOVE {Literal(files.Single(x => x.Type == "L").Logical)} TO {Literal(log)}, CHECKSUM, RECOVERY;");
        await Execute($"DBCC CHECKDB ([{target}]) WITH NO_INFOMSGS, MAXDOP = 1;");
        return Connection("restored");

        async Task Execute(string text) { await using var command = new SqlCommand(text, sql) { CommandTimeout = 90 }; await command.ExecuteNonQueryAsync(); }
        static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
    }

    public async Task StopOwnedServiceAsync(string name)
    {
        var child = children.Single(item => item.Name == name);
        if (!child.Process.HasExited) { child.Process.Kill(entireProcessTree: true); await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        child.Process.WaitForExit();
        lock (child.Log) child.Log.Dispose(); child.Process.Dispose(); children.Remove(child);
    }

    public Task RestartOwnedServiceAsync(string name)
    {
        if (children.Any(item => item.Name == name) || !projects.TryGetValue(name, out var project)) throw new InvalidOperationException("Only a stopped service owned by this QA host can be restarted.");
        return StartService(name, project);
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        foreach (var (_, process, log) in children.AsEnumerable().Reverse())
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } process.WaitForExit(); }
            catch (Exception error) { failures.Add(error); }
            finally { lock (log) log.Dispose(); process.Dispose(); }
        }
        var master = new SqlConnectionStringBuilder(Connection("messaging")) { InitialCatalog = "master" };
        await using var sql = new SqlConnection(master.ConnectionString);
        try
        {
            await sql.OpenAsync();
            foreach (var database in ownedDatabases.AsEnumerable().Reverse())
            {
                // Both exact ownership and a fresh run-specific name are required before deletion.
                if (!databases.Values.Contains(database) || !database.StartsWith($"ETranslatePaginationQa_{RunId}_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Database cleanup ownership check failed.");
                await using var drop = new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END", sql) { CommandTimeout = 30 };
                try { await drop.ExecuteNonQueryAsync(); } catch (Exception error) { failures.Add(error); }
            }
        }
        catch (Exception error) { failures.Add(error); }
        await File.WriteAllTextAsync(Path.Combine(Output, "cleanup.json"), System.Text.Json.JsonSerializer.Serialize(new
        { runId = RunId, databasesCreated = ownedDatabases.Count, cleanupSucceeded = failures.Count == 0, existingDatabasesUsed = false }));
        if (failures.Count > 0) throw new AggregateException("Isolated QA cleanup failed; inspect cleanup.json.", failures);
    }
}
