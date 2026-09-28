using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Infrastructure.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Notifications;

/// <summary>
/// Fallback channel. Reuses the Resend HTTPS API already wired up for login
/// codes (no SMTP, which DigitalOcean blocks outbound on). Not an
/// <see cref="INotifier"/> itself — it needs a resolved address, which
/// <see cref="PushOrEmailNotifier"/> owns.
/// </summary>
public class EmailNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailHttpOptions> options,
    ILogger<EmailNotifier> log)
{
    /// <summary>False when no Resend key is configured (dev / E2E / CI).</summary>
    public bool Enabled => !string.IsNullOrEmpty(options.Value.ApiKey);

    public virtual async Task<NotifyResult> SendToAsync(
        string email, string name, string title, string body, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient("Resend");

        using var response = await client.PostAsJsonAsync("emails", new ResendSendRequest(
            opts.From,
            [email],
            title,
            $"""
             <p>Hi {WebUtility.HtmlEncode(name)},</p>
             <p>{BodyHtml(body)}</p>
             """), ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            log.LogError("Resend send to {Email} failed: {Status} {Body}",
                email, (int)response.StatusCode, responseBody);
            throw new HttpRequestException($"Resend API returned {(int)response.StatusCode}");
        }

        var id = await ReadIdAsync(response, ct);
        log.LogInformation("Notification emailed to {Email} ({Title})", email, title);
        return new NotifyResult("email", id);
    }

    private static async Task<string?> ReadIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Renders a plain-text body as HTML: encode, then keep newlines.</summary>
    public static string BodyHtml(string body) =>
        WebUtility.HtmlEncode(body).Replace("\n", "<br>");
}
