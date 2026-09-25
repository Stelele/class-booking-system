using Application;
using Endpoints;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var configuredDbPath = builder.Configuration["App:DbPath"] ?? "data/booking.db";
var keyDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configuredDbPath))!, "data-protection-keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("ClassBooking");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 403; }
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();
app.MapGoogleLogin();
app.MapGoogleAuth();
app.MapSlots();
app.MapIcs();
app.MapBookings();
app.MapAdmin();

if (app.Environment.EnvironmentName == "E2E")
{
    app.MapGet("/api/test/latest-code/{email}", (string email) =>
        Results.Json(new { code = Infrastructure.Auth.E2eCodeStore.Last }));
}

await app.MigrateAndSeedAsync();
app.Run();

public partial class Program { }
