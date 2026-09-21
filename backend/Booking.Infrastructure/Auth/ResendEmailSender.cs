using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Booking.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Infrastructure.Auth;

public sealed class EmailHttpOptions
{
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "";
}

/// Sends via the Resend HTTPS API (port 443 — immune to DO's outbound SMTP
/// blocks). Port of erpnext-dashboard's HttpEmailSender; giftmugweni.com is
/// already a verified Resend domain for that key.
public sealed class ResendEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailHttpOptions> options,
    ILogger<ResendEmailSender> log) : ICodeSender
{
    public async Task SendAsync(string email, string name, string code, CancellationToken ct = default)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient("Resend");

        using var response = await client.PostAsJsonAsync("emails", new ResendSendRequest(
            opts.From,
            [email],
            "Your booking login code",
            $"""
            <p>Hi {name},</p>
            <p>Your login code is <strong style="font-size:1.4em">{code}</strong>. It expires in 10 minutes.</p>
            """), ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            log.LogError("Resend send to {Email} failed: {Status} {Body}", email, (int)response.StatusCode, body);
            throw new HttpRequestException($"Resend API returned {(int)response.StatusCode}");
        }
        log.LogInformation("Login code emailed to {Email}", email);
    }
}

internal sealed record ResendSendRequest(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string[] To,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string Html);
