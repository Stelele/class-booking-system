using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Auth;

public sealed class EmailCodeSender(IConfiguration config, ILogger<EmailCodeSender> log) : ICodeSender
{
    public async Task SendAsync(string email, string name, string code, CancellationToken ct = default)
    {
        var smtp = config.GetSection("Smtp");
        var host = smtp["Host"];
        if (string.IsNullOrEmpty(host))
        {
            log.LogWarning("SMTP not configured — code for {Email}: {Code}", email, code);
            return;
        }
        using var msg = new System.Net.Mail.MailMessage(
            from: smtp["From"] ?? "bookings@example.com",
            to: email,
            subject: "Your booking login code",
            body: $"Hi {name}, your login code is {code}. It expires in 10 minutes.");
        using var client = new System.Net.Mail.SmtpClient(host, int.Parse(smtp["Port"] ?? "587"));
        client.EnableSsl = true;
        var user = smtp["User"];
        var password = smtp["Password"];
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            client.Credentials = new System.Net.NetworkCredential(user, password);
        await client.SendMailAsync(msg, ct);
    }
}
