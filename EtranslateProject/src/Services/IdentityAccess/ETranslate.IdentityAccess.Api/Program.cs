using System.Text.Json.Serialization;
using ETranslate.IdentityAccess.Api.Endpoints;
using ETranslate.IdentityAccess.Api.Identity;
using ETranslate.IdentityAccess.Api.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);

var identityConnectionString = builder.Configuration.GetConnectionString("identitydb")
    ?? throw new InvalidOperationException("Connection string 'identitydb' is not configured.");
var messagingConnectionString = builder.Configuration.GetConnectionString("messaging")
    ?? throw new InvalidOperationException("Connection string 'messaging' is not configured.");

builder.Services.AddDbContext<IdentityAccessDbContext>(options =>
    options.UseNpgsql(identityConnectionString, npgsql => npgsql.EnableRetryOnFailure()));

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityAccessDbContext>();

builder.Services.AddAuthorization();
builder.Services.AddMassTransit(configuration =>
{
    configuration.AddEntityFrameworkOutbox<IdentityAccessDbContext>(outbox =>
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
app.UseAuthentication();
app.UseAuthorization();

await app.ApplyDatabaseMigrationsAsync();

app.MapGet("/", () => Results.Ok(new { service = "identity-access", status = "healthy" }));
app.MapGroup("/api/v1/auth").MapIdentityApi<ApplicationUser>();
app.MapTenantEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
