using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var messagingConnectionString = builder.Configuration.GetConnectionString("messaging")
    ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");
await EnsureDatabaseExistsAsync(messagingConnectionString);

var identityDatabase = builder.AddConnectionString("identitydb");
var billingDatabase = builder.AddConnectionString("billingdb");
var workflowDatabase = builder.AddConnectionString("workflowdb");
var documentsDatabase = builder.AddConnectionString("documentsdb");
var trustDatabase = builder.AddConnectionString("trustdb");
var messaging = builder.AddConnectionString("messaging");

var identityAccess = builder.AddProject<Projects.ETranslate_IdentityAccess_Api>("identity-access")
    .WithReference(identityDatabase)
    .WithReference(messaging);
var translationWorkflow = builder.AddProject<Projects.ETranslate_TranslationWorkflow_Api>("translation-workflow")
    .WithReference(workflowDatabase)
    .WithReference(messaging)
    .WithReference(identityAccess)
    .WaitFor(identityAccess);
var documents = builder.AddProject<Projects.ETranslate_Documents_Api>("documents")
    .WithEnvironment("PLAYWRIGHT_BROWSERS_PATH", builder.Configuration["PdfBrowserPath"] ??
        Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../../..", ".local", "pdf-browsers")))
    .WithReference(documentsDatabase)
    .WithReference(messaging)
    .WithReference(identityAccess)
    .WithReference(translationWorkflow)
    .WaitFor(identityAccess)
    .WaitFor(translationWorkflow);
var trust = builder.AddProject<Projects.ETranslate_Trust_Api>("trust")
    .WithReference(trustDatabase).WithReference(messaging).WithReference(identityAccess)
    .WithReference(documents).WithReference(translationWorkflow)
    .WaitFor(identityAccess).WaitFor(documents).WaitFor(translationWorkflow);
var billing = builder.AddProject<Projects.ETranslate_Billing_Api>("billing")
    .WithReference(billingDatabase)
    .WithReference(messaging);
var notaryIntegration = builder.AddProject<Projects.ETranslate_NotaryIntegration_Api>("notary-integration");

var gateway = builder.AddProject<Projects.ETranslate_Gateway>("gateway")
    .WithReference(identityAccess)
    .WithReference(translationWorkflow)
    .WithReference(documents)
    .WithReference(trust)
    .WithReference(billing)
    .WithReference(notaryIntegration)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.ETranslate_Web>("web")
    .WithReference(trust)
    .WithReference(identityAccess)
    .WithReference(translationWorkflow)
    .WithReference(documents)
    .WaitFor(identityAccess)
    .WaitFor(translationWorkflow)
    .WaitFor(documents)
    .WaitFor(trust)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.ETranslate_Notifications_Worker>("notifications")
    .WithReference(identityAccess)
    .WithReference(translationWorkflow)
    .WithReference(documents)
    .WithReference(trust)
    .WithReference(billing)
    .WithReference(notaryIntegration);

builder.Build().Run();

static async Task EnsureDatabaseExistsAsync(string connectionString)
{
    var databaseConnection = new SqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(databaseConnection.InitialCatalog))
    {
        throw new InvalidOperationException(
            "The messaging connection string must specify a database name.");
    }

    var databaseName = databaseConnection.InitialCatalog;
    var systemConnection = new SqlConnectionStringBuilder(connectionString)
    {
        InitialCatalog = "master"
    };

    await using var connection = new SqlConnection(systemConnection.ConnectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        IF DB_ID(@databaseName) IS NULL
        BEGIN
            DECLARE @statement nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
            EXEC sys.sp_executesql @statement;
        END;
        """;
    command.Parameters.AddWithValue("@databaseName", databaseName);
    await command.ExecuteNonQueryAsync();
}
