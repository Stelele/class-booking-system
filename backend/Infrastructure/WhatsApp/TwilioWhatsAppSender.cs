using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.WhatsApp;

public sealed class TwilioWhatsAppSender(
    HttpClient http,
    IOptions<TwilioOptions> options,
    ILogger<TwilioWhatsAppSender> log) : ITwilioSender
{
    public async Task<string> SendAsync(string toE164, string body, CancellationToken ct)
    {
        var opts = options.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"2010-04-01/Accounts/{opts.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["From"] = $"whatsapp:{opts.FromNumber}",
                ["To"] = $"whatsapp:{toE164}",
                ["Body"] = body,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.AccountSid}:{opts.AuthToken}")));
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Twilio send to {To} failed: {Status} {Body}", toE164, (int)res.StatusCode, err);
            throw new HttpRequestException($"Twilio returned {(int)res.StatusCode}");
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var sid = doc.RootElement.GetProperty("sid").GetString() ?? "";
        log.LogInformation("WhatsApp sent to {To} sid {Sid}", toE164, sid);
        return sid;
    }
}
