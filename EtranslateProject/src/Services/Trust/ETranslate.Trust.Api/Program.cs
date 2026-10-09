using System.Text.Json.Serialization;
using ETranslate.Trust.Api.Dependencies;
using ETranslate.Trust.Api.Endpoints;
using ETranslate.Trust.Api.Persistence;
using ETranslate.Trust.Api.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISignatureProviderCapabilities, UnconfiguredSignatureProvider>();
builder.Services.AddScoped<TrustDependencyClient>();
foreach (var service in new[] { "identity-access", "documents", "translation-workflow" })
    builder.Services.AddHttpClient(service, client => client.BaseAddress = new Uri($"https+http://{service}"));
var trustConnection = builder.Configuration.GetConnectionString("trustdb") ?? throw new InvalidOperationException("Connection string 'trustdb' is not configured.");
var messagingConnection = builder.Configuration.GetConnectionString("messaging") ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");
builder.Services.AddDbContext<TrustDbContext>(options => options.UseSqlServer(trustConnection, sql => sql.EnableRetryOnFailure()));
builder.Services.AddOptions<SqlTransportOptions>().Configure(options => options.ConnectionString = messagingConnection);
builder.Services.AddSqlServerMigrationHostedService(options => options.CreateDatabase = false);
builder.Services.AddMassTransit(configuration =>
{
    configuration.AddEntityFrameworkOutbox<TrustDbContext>(outbox => { outbox.UseSqlServer(); outbox.UseBusOutbox(); });
    configuration.UsingSqlServer((context, sql) => sql.ConfigureEndpoints(context));
});

var app = builder.Build();
app.UseExceptionHandler();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<TrustDbContext>().Database.MigrateAsync();

app.MapGet("/", () => Results.Ok(new { service = "trust", status = "healthy" }));
app.MapDefaultEndpoints();
app.MapSigningPreparationEndpoints();

app.Run();

public partial class Program;
