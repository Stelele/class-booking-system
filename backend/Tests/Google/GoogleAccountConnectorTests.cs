using System.Net;
using Application.Abstractions;
using Infrastructure.Google;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Google;

public sealed class GoogleAccountConnectorTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public async Task Disconnect_revokes_refresh_token_and_deletes_local_token()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        string? body = null;
        var connector = Build(crypto, store, request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.True(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
        Assert.Contains("token=1//refresh", Uri.UnescapeDataString(body!));
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_remote_revocation_fails()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        var connector = Build(crypto, store, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_token_cannot_be_decrypted()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(new GoogleTokenData(
            "not-valid-ciphertext", "access", DateTime.UtcNow.AddHours(1), false));
        var connector = Build(crypto, store, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_request_is_cancelled()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        var connector = Build(crypto, store, _ => throw new OperationCanceledException());

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_without_token_is_idempotent()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(null);
        var connector = Build(crypto, store, _ => throw new InvalidOperationException("HTTP must not be called"));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.True(result);
        Assert.True(store.Deleted);
    }

    private static GoogleTokenData Token(GoogleTokenCrypto crypto, string refreshToken) =>
        new(crypto.Encrypt(refreshToken), "access", DateTime.UtcNow.AddHours(1), false);

    private static GoogleAccountConnector Build(
        GoogleTokenCrypto crypto,
        FakeStore store,
        Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(
            new GoogleOAuthClient(new HttpClient()),
            Options.Create(new GoogleOAuthSettings()),
            crypto,
            store,
            new StubHttpClientFactory(new HttpClient(new StubHandler(response))),
            NullLogger<GoogleAccountConnector>.Instance);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response(request));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeStore(GoogleTokenData? current) : IGoogleTokenStore
    {
        public GoogleTokenData? Current { get; private set; } = current;
        public bool Deleted { get; private set; }

        public Task<GoogleTokenData?> GetAsync(CancellationToken ct) =>
            Task.FromResult(Current);

        public Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FlagReconnectAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken ct)
        {
            Current = null;
            Deleted = true;
            return Task.CompletedTask;
        }
    }
}
