using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Booking.Application.Abstractions;
using Booking.Infrastructure.Google;
using Booking.Infrastructure.Meet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Google;

public class GoogleCalendarProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(fn(r));
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/")
        };
    }

    private sealed class FakeStore(GoogleTokenData? seed) : IGoogleTokenStore
    {
        public GoogleTokenData? Current = seed;
        public bool ReconnectFlagged { get; private set; }

        public Task<GoogleTokenData?> GetAsync(CancellationToken ct) => Task.FromResult(Current);

        public Task SaveAsync(GoogleTokenData token, CancellationToken ct)
        {
            Current = token;
            return Task.CompletedTask;
        }

        public Task FlagReconnectAsync(CancellationToken ct)
        {
            ReconnectFlagged = true;
            if (Current is not null) Current = Current with { NeedsReconnect = true };
            return Task.CompletedTask;
        }
    }

    private string? _body;
    private string? _uri;

    private GoogleCalendarProvider Build(Func<HttpRequestMessage, HttpResponseMessage> fn, FakeStore store)
    {
        var handler = new StubHandler(req =>
        {
            _uri = req.RequestUri?.ToString();
            _body = req.Content is null
                ? null
                : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return fn(req);
        });
        var oauthHttp = new HttpClient(handler, disposeHandler: false)
            { BaseAddress = new Uri("https://oauth2.googleapis.com/") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                { ["App:FixedMeetLink"] = "https://meet.google.com/fixed-link" })
            .Build();
        return new GoogleCalendarProvider(
            new StubFactory(handler),
            store,
            new GoogleOAuthClient(oauthHttp),
            Options.Create(new GoogleOAuthSettings
                { ClientId = "cid", ClientSecret = "csec", RedirectUri = "https://x/cb" }),
            new GoogleTokenCrypto(RandomNumberGenerator.GetBytes(32)),
            NullLogger<GoogleCalendarProvider>.Instance,
            new FixedLinkMeetProvider(config));
    }

    [Fact]
    public async Task Insert_returns_meet_link_and_event_id()
    {
        var store = new FakeStore(new GoogleTokenData("enc", "ya29.valid", DateTime.UtcNow.AddHours(1), false));
        var sut = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    id = "evt_123",
                    conferenceData = new
                    {
                        entryPoints = new[]
                            { new { entryPointType = "video", uri = "https://meet.google.com/abc-defg-hij" } }
                    }
                }),
                Encoding.UTF8, "application/json"),
        }, store);

        var result = await sut.GetOrCreateLinkAsync(new DateOnly(2026, 9, 30));

        Assert.Equal("https://meet.google.com/abc-defg-hij", result.MeetLink);
        Assert.Equal("evt_123", result.GoogleEventId);
        Assert.Contains("conferenceDataVersion=1", _uri ?? "");
        Assert.Contains("hangoutsMeet", _body ?? "");
        Assert.Contains("Africa/Harare", _body ?? "");
        Assert.Contains("T20:30:00", _body ?? "");
    }

    [Fact]
    public async Task Calendar_401_falls_back_and_flags_reconnect()
    {
        var store = new FakeStore(new GoogleTokenData("enc", "ya29.stale", DateTime.UtcNow.AddHours(1), false));
        var sut = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized), store);

        var result = await sut.GetOrCreateLinkAsync(new DateOnly(2026, 9, 30));

        Assert.Equal("https://meet.google.com/fixed-link", result.MeetLink);
        Assert.Null(result.GoogleEventId);
        Assert.True(store.ReconnectFlagged);
    }

    [Fact]
    public async Task Delete_missing_event_is_swallowed()
    {
        var store = new FakeStore(new GoogleTokenData("enc", "ya29.valid", DateTime.UtcNow.AddHours(1), false));
        var sut = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound), store);

        var ex = await Record.ExceptionAsync(() => sut.DeleteEventAsync("evt_gone"));

        Assert.Null(ex);
        Assert.Contains("evt_gone", _uri ?? "");
    }
}
