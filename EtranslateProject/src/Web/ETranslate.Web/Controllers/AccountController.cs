using System.Security.Claims;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed class AccountController(BackendApi api) : Controller
{
    [HttpGet] public IActionResult Login() => View(new LoginInput());
    [HttpGet] public IActionResult Register() => View("Login", new LoginInput());
    [HttpPost] public Task<IActionResult> Login(LoginInput input) => Authenticate(input, false);
    [HttpPost] public Task<IActionResult> Register(LoginInput input) => Authenticate(input, true);

    private async Task<IActionResult> Authenticate(LoginInput input, bool register)
    {
        if (!ModelState.IsValid)
        {
            input.Password = "";
            ModelState.Remove(nameof(input.Password));
            return View("Login", input);
        }
        try
        {
            if (register)
            {
                using var registration = await api.SendAsync("identity-access", HttpMethod.Post, "/api/v1/auth/register",
                    JsonContent.Create(new { input.Email, input.Password }), anonymous: true);
                BackendApi.EnsureSuccess(registration);
            }
            using var response = await api.SendAsync("identity-access", HttpMethod.Post, "/api/v1/auth/login",
                JsonContent.Create(new { input.Email, input.Password }), anonymous: true);
            BackendApi.EnsureSuccess(response);
            var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
            var properties = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
                AllowRefresh = false
            };
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token.AccessToken }]);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, input.Email)],
                    CookieAuthenticationDefaults.AuthenticationScheme)), properties);
            return RedirectToAction("Index", "Workspace");
        }
        catch (BackendException error)
        {
            ModelState.AddModelError("", error.Status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized
                ? register ? "ثبت‌نام انجام نشد. ایمیل معتبر و رمز شامل حرف بزرگ، کوچک، عدد و نشانه وارد کنید؛ ایمیل ممکن است قبلاً ثبت شده باشد."
                    : "ایمیل یا رمز نادرست است؛ دوباره بررسی کنید." : error.UserMessage);
            input.Password = "";
            ModelState.Remove(nameof(input.Password));
            return View("Login", input);
        }
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
