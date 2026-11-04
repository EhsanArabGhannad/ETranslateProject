using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETranslate.Contracts.Billing;
using ETranslate.Contracts.Documents;
using ETranslate.Contracts.Trust;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Endpoints;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Documents.Api.Storage;
using ETranslate.SubscriptionAccess;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalStorageFailureFactAttribute : FactAttribute
{
    public LocalStorageFailureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_FAILURE_TESTS") != "1") Skip = "Run Verify-StorageFailures.ps1; new QA databases only.";
        Timeout = 300000;
    }
}

// Production endpoint mappings, SQL provider/migrations/outbox and local blob store.
// Identity/Billing/Trust and PDF rendering are explicit test doubles, not a six-service acceptance.
public sealed class DocumentStorageFailureAcceptanceTests
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nSTORAGE FAULT TEST DOUBLE - NOT A RENDERED DOCUMENT\n%%EOF");
    private const string Body = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"STORAGE_QA İstanbul ترجمه\"}]}]}";
    private readonly Guid tenant = Guid.NewGuid(), actor = Guid.NewGuid();
    private readonly List<object> cases = [];
    private readonly Dependencies dependencies = new();

    [LocalStorageFailureFact]
    public async Task Endpoint_failures_preserve_committed_blobs_and_manual_retries_recover()
    {
        var root = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_FAILURE_ROOT") ?? throw new InvalidOperationException("Solution root required");
        var server = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_FAILURE_SERVER") ?? @".\ESIMSSQLSERVER";
        await using var owned = new IsolatedSqlProductHost(server, root, "storage-failures");
        await owned.CreateDatabasesAsync();
        var fault = new SqlFault(owned.Connection("documentsdb"));
        var store = new FaultStore(new LocalDocumentBlobStore(new() { RootPath = owned.Storage }));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<DocumentsDbContext>(options => options.UseSqlServer(owned.Connection("documentsdb"), sql => sql.EnableRetryOnFailure()).AddInterceptors(fault));
        builder.Services.AddSingleton<IDocumentBlobStore>(store); builder.Services.AddSingleton<DocumentBlobWrites>();
        dependencies.Tenant = tenant; dependencies.Actor = actor;
        builder.Services.AddSingleton(new TenantAccessClient(dependencies.Client()));
        builder.Services.AddSingleton(new SubscriptionWriteClient(dependencies.Client(), TimeProvider.System));
        builder.Services.AddSingleton(new SigningPlanClient(dependencies.Client()));
        builder.Services.AddSingleton(new TranslationJobClient(dependencies.Client()));
        builder.Services.AddSingleton<IDraftPdfRenderer, StubRenderer>(); builder.Services.AddSingleton<ISigningPreviewRenderer, StubRenderer>();
        builder.Services.AddMassTransit(bus => {
            bus.AddEntityFrameworkOutbox<DocumentsDbContext>(outbox => { outbox.UseSqlServer(); outbox.UseBusOutbox(delivery => delivery.DisableDeliveryService()); });
            bus.UsingInMemory((context, transport) => transport.ConfigureEndpoints(context));
        });
        await using var app = builder.Build(); app.UseExceptionHandler(); app.MapDocumentEndpoints(); app.MapDocumentTemplateEndpoints();
        await using (var db = Db(owned)) {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.QaDuplicate (Id int NOT NULL PRIMARY KEY); INSERT INTO dbo.QaDuplicate VALUES (1);");
        }
        await app.StartAsync();
        using var api = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(30) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-owner");
        try
        {
            foreach (var kind in new[] { "source", "image", "template", "pdf", "preview" })
            foreach (var mode in new[] { "store-before", "store-after", "sql-before", "sql-after", "sql-after-cancel", "sql-after-duplicate" })
            {
                var sample = await Seed(owned, kind); var before = await Snapshot(owned, sample);
                var files = Directory.GetFiles(owned.Storage, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
                store.Mode = mode; fault.Mode = mode;
                using var response = await Send(api, sample);
                var committed = mode is "sql-after" or "sql-after-cancel" or "sql-after-duplicate";
                var recoveredConflict = mode == "sql-after-duplicate" && kind != "template";
                Assert.True((int)response.StatusCode == (recoveredConflict ? 200 : 500), $"{kind}/{mode}: unexpected HTTP {(int)response.StatusCode}");
                Assert.Equal(mode.StartsWith("sql-", StringComparison.Ordinal) ? 1 : 0, fault.Hits);
                store.Mode = ""; fault.Mode = "";
                var saved = await Assets(owned, sample);
                Assert.Equal(committed ? 1 : 0, saved.Length);
                var addedFiles = Directory.GetFiles(owned.Storage, "*", SearchOption.AllDirectories).Where(path => !files.Contains(path)).ToArray();
                // A pre-SQL exception after PersistenceStarting is deliberately treated as unknown.
                Assert.Equal(mode.StartsWith("sql-", StringComparison.Ordinal) ? 1 : 0, addedFiles.Length);
                if (committed)
                {
                    var asset = Assert.Single(saved); await VerifyBytes(api, sample, asset);
                    // Exercise the fresh-reference probe for every supported metadata table.
                    await using (var write = app.Services.GetRequiredService<DocumentBlobWrites>().Begin(asset.Key)) {
                        write.PersistenceStarting(); await write.DiscardAfterConflictAsync();
                    }
                    Assert.True(File.Exists(Path.Combine(owned.Storage, asset.Key)));
                    store.ReadFailure = true;
                    using var unavailable = await api.GetAsync(sample.Download(asset.Id)); Assert.Equal(kind == "preview" ? 503 : 500, (int)unavailable.StatusCode);
                    store.ReadFailure = false; await VerifyBytes(api, sample, asset);
                    if (kind == "template") {
                        using var list = await api.GetAsync(sample.Collection); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
                        Assert.Single((await list.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
                    }
                    else {
                        using var retry = await Send(api, sample); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                        Assert.Equal(asset.Id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
                    }
                }
                else
                {
                    using var retry = await Send(api, sample); Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
                    await VerifyBytes(api, sample, Assert.Single(await Assets(owned, sample)));
                }
                Assert.Single(await Assets(owned, sample));
                Assert.Equal(before, await Snapshot(owned, sample));
                cases.Add(new { kind, mode, response = (int)response.StatusCode, committed, retainedUnreferenced = mode == "sql-before", manualRecovery = true, passed = true });
            }
            foreach (var kind in new[] { "source", "image" })
            {
                var sample = await Seed(owned, kind); var files = Directory.GetFiles(owned.Storage, "*", SearchOption.AllDirectories).Length;
                fault.Mode = "sql-stale"; fault.Document = sample.Document;
                using var stale = await Send(api, sample); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(1, fault.Hits); fault.Mode = "";
                Assert.Empty(await Assets(owned, sample)); Assert.Equal(files, Directory.GetFiles(owned.Storage, "*", SearchOption.AllDirectories).Length);
                using var retry = await Send(api, sample); Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
                await VerifyBytes(api, sample, Assert.Single(await Assets(owned, sample)));
                cases.Add(new { kind, mode = "real-sql-rowversion-conflict", losingBlobDeleted = true, passed = true });
            }
            await VerifyAllFiles(owned);
            Assert.Equal(32, cases.Count);
            var latest = Path.Combine(root, "tmp/storage-failures"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = owned.RunId }));
        }
        finally
        {
            await app.StopAsync();
            await File.WriteAllTextAsync(Path.Combine(owned.Output, "storage-report.json"), JsonSerializer.Serialize(new {
                runId = owned.RunId, cases, existingDatabasesUsed = false, dependenciesAndRenderingAreTestDoubles = true,
                sqlAndFilesystemAreReal = true, retainedOrphansExpected = 5
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed record Asset(Guid Id, string Key, string Hash);
    private sealed record Sample(string Kind, Guid Job, Guid Document, Guid Template, Guid Upload, Guid Preparation, string Base)
    {
        public string Collection => Kind == "template" ? Base + "/assets" : Base + (Kind == "source" ? "/source-files" : Kind == "image" ? "/images" : Kind == "preview" ? "/signing-previews" : "/pdfs");
        public string Post => Kind switch { "source" => Collection + "?uploadId=" + Upload, "image" => Collection + "?expectedRevision=1&uploadId=" + Upload,
            "template" => Collection, "pdf" => Base + "/draft-revisions/1/pdfs", _ => Base + $"/signing-preparations/{Preparation}/preview-pdfs" };
        public string Download(Guid id) => Collection + "/" + id + (Kind == "preview" ? "/file" : "");
    }
    private async Task<Sample> Seed(IsolatedSqlProductHost owned, string kind)
    {
        var now = DateTimeOffset.UtcNow; var job = Guid.NewGuid(); var doc = TranslationDocument.Create(tenant, job, actor, now);
        var draft = doc.AddDraftRevision(0, Body, null, actor, now, DocumentContentDescriptor.RichFormat, 10);
        var template = DocumentTemplate.Create(tenant, "Synthetic storage QA " + Guid.NewGuid().ToString("N"), null, "{}", null, null, "{}", null, actor, now);
        var preparation = Guid.NewGuid(); await using var db = Db(owned); db.AddRange(doc, template);
        if (kind == "preview") {
            var key = $"{tenant:N}/{doc.Id:N}/fixture-source.pdf";
            var stored = await new LocalDocumentBlobStore(new() { RootPath = owned.Storage }).SaveAsync(key, new MemoryStream(PdfBytes), "application/pdf", 10000, default);
            var pdf = new DocumentPdfVersion(doc.Id, draft.Id, 1, null, DraftPdfPolicy.RendererVersion, key, stored.SizeBytes, stored.Sha256, actor, now);
            var review = doc.SubmitReview(1, 0, pdf, null, "[]", actor, now); doc.DecideReview(review, DocumentReviewStatus.Approved, actor, null, now);
            db.AddRange(pdf, review);
            dependencies.Plan = new(preparation, tenant, job, doc.Id, "Prepared", 0, "TranslatorOnly", review.Id, 1, pdf.Id, draft.Id, 1,
                null, pdf.Sha256, pdf.SizeBytes, "DraftUnsigned", actor, now, [new(1, "Translator", actor, "Synthetic statement", "EImza")]);
        }
        await db.SaveChangesAsync();
        return new(kind, job, doc.Id, template.Id, Guid.NewGuid(), preparation,
            $"/api/v1/tenants/{tenant}" + (kind == "template" ? $"/document-templates/{template.Id}" : $"/translation-jobs/{job}/documents/{doc.Id}"));
    }
    private static Task<HttpResponseMessage> Send(HttpClient api, Sample sample)
    {
        if (sample.Kind is "pdf" or "preview") return api.PostAsync(sample.Post, null);
        return Upload();
        async Task<HttpResponseMessage> Upload() {
            using var form = new MultipartFormDataContent(); var content = new ByteArrayContent(BodyImageFixture.Png); content.Headers.ContentType = new("image/png");
            form.Add(content, "file", "synthetic.png"); return await api.PostAsync(sample.Post, form);
        }
    }
    private static async Task<Asset[]> Assets(IsolatedSqlProductHost owned, Sample sample)
    {
        await using var db = Db(owned);
        return sample.Kind switch {
            "source" => await db.SourceFiles.Where(x => x.DocumentId == sample.Document).Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync(),
            "image" => await db.DocumentImages.Where(x => x.DocumentId == sample.Document).Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync(),
            "template" => await db.TemplateAssets.Where(x => x.TemplateId == sample.Template).Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync(),
            "pdf" => await db.PdfVersions.Where(x => x.DocumentId == sample.Document).Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync(),
            _ => await db.SigningPreviews.Where(x => x.DocumentId == sample.Document).Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync()
        };
    }
    private static async Task<string> Snapshot(IsolatedSqlProductHost owned, Sample sample)
    {
        await using var db = Db(owned);
        var doc = await db.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == sample.Document);
        var drafts = await db.DraftRevisions.AsNoTracking().Where(x => x.DocumentId == sample.Document).ToArrayAsync();
        var reviews = await db.Reviews.AsNoTracking().Where(x => x.DocumentId == sample.Document).ToArrayAsync();
        return JsonSerializer.Serialize(new { doc.CurrentDraftRevision, doc.TemplateRevisionId, doc.ReviewRound, doc.ReviewStatus, drafts, reviews });
    }
    private static async Task VerifyBytes(HttpClient api, Sample sample, Asset asset) {
        using var response = await api.GetAsync(sample.Download(asset.Id)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.Equal(asset.Hash, Hash(bytes));
        Assert.Equal(sample.Kind is "pdf" or "preview" ? PdfBytes : BodyImageFixture.Png, bytes);
    }
    private static async Task VerifyAllFiles(IsolatedSqlProductHost owned) {
        await using var db = Db(owned);
        var keys = (await db.SourceFiles.Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync())
            .Concat(await db.DocumentImages.Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync())
            .Concat(await db.TemplateAssets.Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync())
            .Concat(await db.PdfVersions.Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync())
            .Concat(await db.SigningPreviews.Select(x => new Asset(x.Id, x.StorageKey, x.Sha256)).ToArrayAsync()).ToArray();
        foreach (var asset in keys) Assert.Equal(asset.Hash, Hash(await File.ReadAllBytesAsync(Path.Combine(owned.Storage, asset.Key))));
        var files = Directory.GetFiles(owned.Storage, "*", SearchOption.AllDirectories); Assert.Equal(keys.Length + 5, files.Length);
        Assert.DoesNotContain(files, path => path.EndsWith(".uploading", StringComparison.Ordinal));
        // Every accepted source/image/template upload and its bus outbox message commit once.
        Assert.Equal(20, await db.Set<OutboxMessage>().CountAsync());
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static DocumentsDbContext Db(IsolatedSqlProductHost owned) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(owned.Connection("documentsdb")).Options);

    private sealed class FaultStore(IDocumentBlobStore inner) : IDocumentBlobStore
    {
        public string Mode = ""; public bool ReadFailure;
        public async Task<StoredBlob> SaveAsync(string key, Stream source, string type, long limit, CancellationToken token) {
            if (Mode == "store-before") throw new IOException("Injected before file publication");
            var saved = await inner.SaveAsync(key, source, type, limit, token);
            if (Mode == "store-after") throw new IOException("Injected after file publication, before SQL");
            return saved;
        }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken token) => ReadFailure ? throw new IOException("Injected storage read outage") : inner.OpenReadAsync(key, token);
        public Task DeleteIfExistsAsync(string key, CancellationToken token) => inner.DeleteIfExistsAsync(key, token);
    }
    private sealed class SqlFault(string connection) : SaveChangesInterceptor
    {
        private string mode = ""; public int Hits; public Guid Document;
        public string Mode { get => mode; set { mode = value; Hits = 0; } }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default) {
            if (mode == "sql-before") { Hits++; throw new IOException("Injected before SQL write"); }
            if (mode == "sql-stale") {
                Hits++; await using var sql = new SqlConnection(connection); await sql.OpenAsync(token);
                await using var update = new SqlCommand("UPDATE documents.translation_documents SET UpdatedAtUtc=DATEADD(second,1,UpdatedAtUtc) WHERE Id=@id", sql);
                update.Parameters.AddWithValue("@id", Document); Assert.Equal(1, await update.ExecuteNonQueryAsync(token));
            }
            return result;
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default) {
            if (!mode.StartsWith("sql-after", StringComparison.Ordinal)) return result;
            Hits++;
            if (mode == "sql-after-cancel") throw new OperationCanceledException("Injected after committed SQL write");
            if (mode == "sql-after-duplicate") {
                await using var sql = new SqlConnection(connection); await sql.OpenAsync(token);
                await using var duplicate = new SqlCommand("INSERT INTO dbo.QaDuplicate VALUES (1)", sql);
                try { await duplicate.ExecuteNonQueryAsync(token); } catch (SqlException error) when (error.Number == 2627) { throw new DbUpdateException("Injected retry duplicate after successful commit", error); }
                throw new InvalidOperationException("Expected SQL duplicate");
            }
            throw new IOException("Injected after committed SQL write");
        }
    }
    private sealed class StubRenderer : IDraftPdfRenderer, ISigningPreviewRenderer
    {
        public Task<byte[]> RenderAsync(DraftPdfInput input, CancellationToken token) => Task.FromResult(PdfBytes);
        public Task<byte[]> RenderAsync(byte[] pdf, SigningPreviewSnapshotV1 plan, Guid id, CancellationToken token) => Task.FromResult(PdfBytes);
    }
    private sealed class Dependencies : HttpMessageHandler
    {
        public Guid Tenant, Actor; public SigningPreviewSnapshotV1? Plan;
        public HttpClient Client() => new(this, disposeHandler: false) { BaseAddress = new("http://synthetic-dependencies.invalid") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            var now = DateTimeOffset.UtcNow; object body = request.RequestUri!.AbsolutePath.EndsWith("preview-snapshot", StringComparison.Ordinal) ? Plan!
                : request.RequestUri.AbsolutePath.EndsWith("write-access", StringComparison.Ordinal) ? new SubscriptionWriteAccessV1(Tenant, "Trialing", true, null, now, now.AddDays(1))
                : new { tenantId = Tenant, userId = Actor, tenantType = "TranslationOffice", role = "Owner" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
