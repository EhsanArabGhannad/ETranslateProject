using System.Text.Json.Serialization;
using ETranslate.TranslationWorkflow.Api.Authorization;
using ETranslate.TranslationWorkflow.Api.Endpoints;
using ETranslate.TranslationWorkflow.Api.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);

var workflowConnectionString = builder.Configuration.GetConnectionString("workflowdb")
    ?? throw new InvalidOperationException("Connection string 'workflowdb' is not configured.");
var messagingConnectionString = builder.Configuration.GetConnectionString("messaging")
    ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");

builder.Services.AddDbContext<TranslationWorkflowDbContext>(options =>
    options.UseSqlServer(workflowConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
builder.Services.AddHttpClient<TenantAccessClient>(client =>
    client.BaseAddress = new Uri("https+http://identity-access"));

builder.Services.AddOptions<SqlTransportOptions>()
    .Configure(options => options.ConnectionString = messagingConnectionString);
builder.Services.AddSqlServerMigrationHostedService(options => options.CreateDatabase = false);

builder.Services.AddMassTransit(configuration =>
{
    configuration.AddEntityFrameworkOutbox<TranslationWorkflowDbContext>(outbox =>
    {
        outbox.UseSqlServer();
        outbox.UseBusOutbox();
    });

    configuration.UsingSqlServer((context, sqlServer) =>
    {
        sqlServer.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseExceptionHandler();
await app.ApplyDatabaseMigrationsAsync();

app.MapGet("/", () => Results.Ok(new { service = "translation-workflow", status = "healthy" }));
app.MapTranslationJobEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
