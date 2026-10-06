using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

// Configure each named backend once: PDF generation needs a longer attempt than ordinary API calls.
builder.AddServiceDefaults(enableHttpResilience: false);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<ApiExceptionFilter>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "ETranslate.Web";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.SlidingExpiration = false;
    });
builder.Services.AddScoped<BackendApi>();
foreach (var service in new[] { "identity-access", "translation-workflow", "documents" })
{
    builder.Services.AddHttpClient(service, client => client.BaseAddress = new Uri($"https+http://{service}"))
        .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
}
// Print rendering has a bounded 20-second budget; do not inherit the generic 10-second attempt limit.
builder.Services.AddHttpClient("documents-pdf", client =>
    client.BaseAddress = new Uri("https+http://documents"))
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(35);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.Retry.DisableForUnsafeHttpMethods();
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'; style-src 'self'; img-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapDefaultEndpoints();


app.Run();
