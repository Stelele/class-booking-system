using System.Security.Cryptography;
using System.Text.Json;
using Application.Abstractions;
using Application.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Endpoints;

public static class GoogleLoginEndpoints
{
    private const string FailureRedirect = "/login?google=error";
    private const string SuccessRedirect = "/calendar";
    private const string StateCookieName = "GoogleLoginState";
    private const int BindingBytes = 32;
    private static readonly TimeSpan StateCookieLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapGoogleLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/google/login/start", async (
            ISender sender, HttpContext http, IHostEnvironment environment, ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("GoogleLogin");
            try
            {
                var binding = NewBinding();
                var url = await sender.Send(new BeginGoogleLoginQuery(binding));
                var options = StateCookieOptions(environment);
                options.MaxAge = StateCookieLifetime;
                http.Response.Cookies.Append(StateCookieName, binding, options);
                return Results.Redirect(url);
            }
            catch (Exception ex) when (IsKnownFailure(ex))
            {
                log.LogWarning("Google login start failed ({ErrorType}).", ex.GetType().Name);
                return Results.Redirect(FailureRedirect);
            }
        });

        app.MapGet("/api/auth/google/login/callback", async (
            string? code, string? state, ISender sender, HttpContext http,
            IHostEnvironment environment, ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("GoogleLogin");
            var binding = http.Request.Cookies[StateCookieName] ?? "";
            http.Response.Cookies.Delete(StateCookieName, StateCookieOptions(environment));
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(binding))
            {
                log.LogWarning("Google login callback missing code, state, or client binding.");
                return Results.Redirect(FailureRedirect);
            }
            try
            {
                var user = await sender.Send(new CompleteGoogleLoginCommand(code, state, binding));
                await AuthCookie.IssueAsync(http, user);
                return Results.Redirect(SuccessRedirect);
            }
            catch (Exception ex) when (IsKnownFailure(ex))
            {
                log.LogWarning("Google login callback failed ({ErrorType}).", ex.GetType().Name);
                return Results.Redirect(FailureRedirect);
            }
        });

        return app;
    }

    private static string NewBinding() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(BindingBytes));

    private static CookieOptions StateCookieOptions(IHostEnvironment environment) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = !environment.IsDevelopment(),
    };

    private static bool IsKnownFailure(Exception ex) =>
        ex is AuthException or UnauthorizedAccessException or InvalidOperationException
            or HttpRequestException or JsonException or NotSupportedException
            or TaskCanceledException;
}
