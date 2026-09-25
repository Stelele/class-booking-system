using System.Security.Claims;
using Application.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Endpoints;

public static class AuthCookie
{
    public static Task IssueAsync(HttpContext http, UserDto user) => http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(IdentityFor(user)),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

    private static ClaimsIdentity IdentityFor(UserDto user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
        ], "cookies");
        if (user.Role == "Admin") identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        return identity;
    }
}
