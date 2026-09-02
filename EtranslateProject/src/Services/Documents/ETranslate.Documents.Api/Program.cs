var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "documents", status = "healthy" }));
app.MapDefaultEndpoints();

app.Run();
