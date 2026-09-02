var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var identityDatabase = postgres.AddDatabase("identitydb");
var billingDatabase = postgres.AddDatabase("billingdb");

var messaging = builder.AddRabbitMQ("messaging");

var identityAccess = builder.AddProject<Projects.ETranslate_IdentityAccess_Api>("identity-access")
    .WithReference(identityDatabase)
    .WithReference(messaging)
    .WaitFor(identityDatabase)
    .WaitFor(messaging);
var translationWorkflow = builder.AddProject<Projects.ETranslate_TranslationWorkflow_Api>("translation-workflow");
var documents = builder.AddProject<Projects.ETranslate_Documents_Api>("documents");
var trust = builder.AddProject<Projects.ETranslate_Trust_Api>("trust");
var billing = builder.AddProject<Projects.ETranslate_Billing_Api>("billing")
    .WithReference(billingDatabase)
    .WithReference(messaging)
    .WaitFor(billingDatabase)
    .WaitFor(messaging);
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
    .WithReference(gateway)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.ETranslate_Notifications_Worker>("notifications")
    .WithReference(identityAccess)
    .WithReference(translationWorkflow)
    .WithReference(documents)
    .WithReference(trust)
    .WithReference(billing)
    .WithReference(notaryIntegration);

builder.Build().Run();
