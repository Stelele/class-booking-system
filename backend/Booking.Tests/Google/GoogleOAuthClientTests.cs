using System.Net;
using System.Text;
using System.Text.Json;
using Booking.Infrastructure.Google;
using Xunit;

public class GoogleOAuthClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(fn(r));
    }

    private string? _capturedBody;
   
    private GoogleOAuthClient Client() => new(new HttpClient(new StubHandler(r =>
    {
        _capturedBody = r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { access_token = "ya29.new", expires_in = 3600, refresh_token = "1//refresh-xyz" }),
                Encoding.UTF8, "application/json"),
        };
    }))
    { BaseAddress = new Uri("https://oauth2.googleapis.com/") });

    [Fact]
    public async Task Exchange_posts_code_grant_and_returns_tokens()
    {
        var tokens = await Client().ExchangeCodeAsync("auth-code-123", "cid", "csec", "https://x/cb", CancellationToken.None);
        Assert.Equal("ya29.new", tokens.AccessToken);
        Assert.Equal("1//refresh-xyz", tokens.RefreshToken);
        Assert.Contains("grant_type=authorization_code", _capturedBody);
        Assert.Contains("code=auth-code-123", _capturedBody);
        var skew = (tokens.ExpiryUtc - DateTime.UtcNow).TotalSeconds;
        Assert.InRange(skew, 3500, 3600);
    }

    [Fact]
    public async Task Refresh_posts_refresh_grant()
    {
        var tokens = await Client().RefreshAsync("1//old", "cid", "csec", CancellationToken.None);
        Assert.Equal("ya29.new", tokens.AccessToken);
        Assert.Contains("grant_type=refresh_token", _capturedBody);
    }
}