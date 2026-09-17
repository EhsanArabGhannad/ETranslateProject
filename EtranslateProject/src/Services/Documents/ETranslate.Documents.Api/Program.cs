using System.Text.Json.Serialization;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Endpoints;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using MassTransit;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 26 * 1024 * 1024);
builder.Services.AddSingleton(TimeProvider.System);

var documentsConnectionString = builder.Configuration.GetConnectionString("documentsdb")
    ?? throw new InvalidOperationException("Connection string 'documentsdb' is not configured.");
var messagingConnectionString = builder.Configuration.GetConnectionString("messaging")
    ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");

builder.Services.AddDbContext<DocumentsDbContext>(options =>
    options.UseNpgsql(documentsConnectionString, npgsql => npgsql.EnableRetryOnFailure()));
builder.Services.AddHttpClient<TenantAccessClient>(client =>
    client.BaseAddress = new Uri("https+http://identity-access"));
builder.Services.AddHttpClient<TranslationJobClient>(client =>
    client.BaseAddress = new Uri("https+http://translation-workflow"));

var configuredStorageRoot = builder.Configuration[$"{DocumentStorageOptions.SectionName}:RootPath"];
var storageRoot = string.IsNullOrWhiteSpace(configuredStorageRoot)
    ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ETranslate",
        "document-blobs")
    : configuredStorageRoot;
builder.Services.AddSingleton(new DocumentStorageOptions { RootPath = storageRoot });
builder.Services.AddSingleton<IDocumentBlobStore, LocalDocumentBlobStore>();

builder.Services.AddMassTransit(configuration =>
{
    configuration.AddEntityFrameworkOutbox<DocumentsDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
    });

    configuration.UsingRabbitMq((context, rabbitMq) =>
    {
        rabbitMq.Host(new Uri(messagingConnectionString));
        rabbitMq.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseExceptionHandler();
await app.ApplyDatabaseMigrationsAsync();

app.MapGet("/", () => Results.Ok(new { service = "documents", status = "healthy" }));
app.MapDocumentEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
