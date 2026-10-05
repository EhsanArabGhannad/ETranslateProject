using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.Web.Services;

// BFF: only server-to-server requests carry the Identity bearer token.
public sealed class BackendApi(IHttpClientFactory clients, IHttpContextAccessor accessor)
{
    public async Task<HttpResponseMessage> SendAsync(string service, HttpMethod method, string path,
        HttpContent? content = null, bool anonymous = false,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!anonymous)
        {
            var token = await accessor.HttpContext!.GetTokenAsync("access_token");
            if (token is null) throw new BackendException(HttpStatusCode.Unauthorized);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        try
        {
            // Buffer small JSON/images; the upstream limits assets to 5 MiB.
            return await clients.CreateClient(service).SendAsync(request, completion, accessor.HttpContext!.RequestAborted);
        }
        catch (HttpRequestException) { throw new BackendException(HttpStatusCode.ServiceUnavailable); }
        catch (TimeoutRejectedException) { throw new BackendException(HttpStatusCode.ServiceUnavailable); }
        catch (BrokenCircuitException) { throw new BackendException(HttpStatusCode.ServiceUnavailable); }
        catch (OperationCanceledException) when (!accessor.HttpContext!.RequestAborted.IsCancellationRequested)
        { throw new BackendException(HttpStatusCode.ServiceUnavailable); }
    }

    public async Task<T> ReadAsync<T>(string service, string path)
    {
        using var response = await SendAsync(service, HttpMethod.Get, path);
        EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(accessor.HttpContext!.RequestAborted))!;
    }
    public async Task<T?> FindAsync<T>(string service, string path) where T : class
    {
        using var response = await SendAsync(service, HttpMethod.Get, path);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(accessor.HttpContext!.RequestAborted);
    }
    public async Task<T> PostAsync<T>(string service, string path, object? data = null)
    {
        using var response = await SendAsync(service, HttpMethod.Post, path, data is null ? null : JsonContent.Create(data));
        EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(accessor.HttpContext!.RequestAborted))!;
    }
    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new BackendException(response.StatusCode);
    }
}
public sealed class BackendException(HttpStatusCode status) : Exception("Backend request failed.")
{
    public HttpStatusCode Status { get; } = status;
    public string UserMessage => Status switch
    {
        HttpStatusCode.BadRequest => "اطلاعات ارسال‌شده معتبر نیست. نام، زبان‌ها و فیلدهای ضروری را بررسی کنید.",
        HttpStatusCode.Conflict => "اطلاعات تغییر کرده یا مورد تکراری است. صفحه را دوباره باز کنید و بررسی کنید.",
        HttpStatusCode.Forbidden => "اجازه‌ی دسترسی یا تغییر این اطلاعات را ندارید.",
        HttpStatusCode.NotFound => "مورد درخواستی در این فضای کاری پیدا نشد.",
        HttpStatusCode.Unauthorized => "نشست ورود پایان یافته؛ دوباره وارد شوید.",
        _ => "سرویس موقتاً در دسترس نیست. کمی بعد دوباره تلاش کنید."
    };
}
public sealed class ApiExceptionFilter : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not BackendException error) return;
        if (error.Status == HttpStatusCode.Unauthorized)
        {
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = new RedirectToActionResult("Login", "Account", null);
        }
        else
        {
            context.Result = new ViewResult
            {
                ViewName = "ApiError",
                StatusCode = (int)error.Status,
                ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState)
                { Model = error.UserMessage }
            };
        }
        context.ExceptionHandled = true;
    }
}
