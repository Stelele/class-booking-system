using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Infrastructure.Google;
using Infrastructure.Meet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Google;

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

        public Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct)
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

        public Task DeleteAsync(CancellationToken ct)
        {
            Current = null;
            return Task.CompletedTask;
        }
    }

    private string? _body;
    private string? _uri;

    // Shared by Build and the refresh tests so seeded RefreshTokenEncrypted
    // values decrypt with the same key the provider uses.
    private static readonly byte[] TestKey = RandomNumberGenerator.GetBytes(32);

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
            new GoogleTokenCrypto(TestKey),
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

    [Fact]
    public async Task Expired_token_triggers_refresh_then_insert()
    {
        var crypto = new GoogleTokenCrypto(TestKey);
        var store = new FakeStore(new GoogleTokenData(
            crypto.Encrypt("refresh-token"), "ya29.stale", DateTime.UtcNow.AddMinutes(-1), false));
        var sut = Build(req =>
        {
            if (req.RequestUri?.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { access_token = "ya29.fresh", expires_in = 3600 }),
                        Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        id = "evt_456",
                        conferenceData = new
                        {
                            entryPoints = new[]
                                { new { entryPointType = "video", uri = "https://meet.google.com/new-meet-link" } }
                        }
                    }),
                    Encoding.UTF8, "application/json"),
            };
        }, store);

        var result = await sut.GetOrCreateLinkAsync(new DateOnly(2026, 9, 30));

        Assert.Equal("https://meet.google.com/new-meet-link", result.MeetLink);
        Assert.Equal("evt_456", result.GoogleEventId);
        Assert.Equal("ya29.fresh", store.Current?.AccessToken);
        Assert.False(store.ReconnectFlagged);
    }

    [Fact]
    public async Task Invalid_grant_refresh_falls_back_and_flags()
    {
        var crypto = new GoogleTokenCrypto(TestKey);
        var store = new FakeStore(new GoogleTokenData(
            crypto.Encrypt("refresh-token"), "ya29.stale", DateTime.UtcNow.AddMinutes(-1), false));
        var sut = Build(req =>
        {
            if (req.RequestUri?.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        id = "evt_should_not_be_used",
                        conferenceData = new
                        {
                            entryPoints = new[]
                                { new { entryPointType = "video", uri = "https://meet.google.com/should-not-be-used" } }
                        }
                    }),
                    Encoding.UTF8, "application/json"),
            };
        }, store);

        var result = await sut.GetOrCreateLinkAsync(new DateOnly(2026, 9, 30));

        Assert.Equal("https://meet.google.com/fixed-link", result.MeetLink);
        Assert.Null(result.GoogleEventId);
        Assert.True(store.ReconnectFlagged);
    }

    [Fact]
    public async Task Transient_500_does_not_flag_reconnect()
    {
        var crypto = new GoogleTokenCrypto(TestKey);
        var store = new FakeStore(new GoogleTokenData(
            crypto.Encrypt("refresh-token"), "ya29.stale", DateTime.UtcNow.AddMinutes(-1), false));
        var sut = Build(req =>
        {
            if (req.RequestUri?.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("transient outage", Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        id = "evt_should_not_be_used",
                        conferenceData = new
                        {
                            entryPoints = new[]
                                { new { entryPointType = "video", uri = "https://meet.google.com/should-not-be-used" } }
                        }
                    }),
                    Encoding.UTF8, "application/json"),
            };
        }, store);

        var result = await sut.GetOrCreateLinkAsync(new DateOnly(2026, 9, 30));

        Assert.Equal("https://meet.google.com/fixed-link", result.MeetLink);
        Assert.Null(result.GoogleEventId);
        Assert.False(store.ReconnectFlagged);
    }
}
