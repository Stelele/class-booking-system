using System.Net;
using System.Text;
using Booking.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class TwilioWhatsAppSenderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(fn(r));
    }

    private string? _body; private string? _auth;
    private TwilioWhatsAppSender Sender() => new(
        new HttpClient(new StubHandler(r =>
        {
            _body = r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            _auth = r.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Created)
            { Content = new StringContent("""{"sid":"SM123"}""", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.twilio.com/") },
        Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+15551234567" }),
        NullLogger<TwilioWhatsAppSender>.Instance);

    [Fact]
    public async Task Posts_whatsapp_form_with_basic_auth_and_returns_sid()
    {
        var sid = await Sender().SendAsync("+447700900123", "Your lesson is tonight", CancellationToken.None);
        Assert.Equal("SM123", sid);
        Assert.StartsWith("Basic ", _auth);
        Assert.Contains("From=whatsapp%3A%2B15551234567", _body);
        Assert.Contains("To=whatsapp%3A%2B447700900123", _body);
        Assert.Contains("Your+lesson+is+tonight", _body);
    }

    [Fact]
    public async Task Non_success_throws_with_status()
    {
        var sender = new TwilioWhatsAppSender(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("""{"message":"bad number"}""", Encoding.UTF8, "application/json") }))
            { BaseAddress = new Uri("https://api.twilio.com/") },
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+1555" }),
            NullLogger<TwilioWhatsAppSender>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("+447700900123", "x", CancellationToken.None));
    }
}
