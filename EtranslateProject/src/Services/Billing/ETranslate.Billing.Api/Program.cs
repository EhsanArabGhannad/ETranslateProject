using System.Text.Json.Serialization;
using ETranslate.Billing.Api.Consumers;
using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Endpoints;
using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Workers;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Billing.Api.Orders;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var billingConnectionString = builder.Configuration.GetConnectionString("billingdb")
    ?? throw new InvalidOperationException("Connection string 'billingdb' is not configured.");
var messagingConnectionString = builder.Configuration.GetConnectionString("messaging")
    ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<SubscriptionAccessClient>(client => client.BaseAddress = new Uri("https+http://identity-access"));
builder.Services.AddDbContextFactory<BillingDbContext>(options =>
    options.UseSqlServer(billingConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()), ServiceLifetime.Scoped);
builder.Services.AddSingleton(new BillingPlanCatalog(builder.Configuration.GetSection("SubscriptionPlans").Get<BillingPlansOptions>()?.Plans ?? []));
builder.Services.AddScoped<PaidPeriodApplicationService>();
builder.Services.AddScoped<PurchaseOrderService>();

builder.Services.AddOptions<SqlTransportOptions>()
    .Configure(options => options.ConnectionString = messagingConnectionString);
builder.Services.AddSqlServerMigrationHostedService(options => options.CreateDatabase = false);

builder.Services.AddMassTransit(configuration =>
{
    configuration.AddConsumer<TenantCreatedConsumer>();
    configuration.AddEntityFrameworkOutbox<BillingDbContext>(outbox =>
    {
        outbox.UseSqlServer();
        outbox.UseBusOutbox();
    });
    configuration.AddConfigureEndpointsCallback((context, _, endpoint) =>
    {
        endpoint.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
        endpoint.UseEntityFrameworkOutbox<BillingDbContext>(context);
    });

    configuration.UsingSqlServer((context, sqlServer) =>
    {
        sqlServer.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<TrialExpirationWorker>();

var app = builder.Build();

// Development minimal-API binding throws for malformed/unknown JSON. Preserve its
// client-error status instead of converting a rejected request into a server error.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = error => error is BadHttpRequestException badRequest
        ? badRequest.StatusCode : StatusCodes.Status500InternalServerError
});
await app.ApplyDatabaseMigrationsAsync();

app.MapGet("/", () => Results.Ok(new { service = "billing", status = "healthy" }));
app.MapSubscriptionEndpoints();
app.MapPurchaseOrderEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
