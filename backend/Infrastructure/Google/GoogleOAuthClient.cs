using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;

namespace Infrastructure.Google;

public sealed record GoogleTokens(string AccessToken, string? RefreshToken, DateTime ExpiryUtc);

public sealed class GoogleOAuthClient(HttpClient http)
{
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    public async Task<GoogleTokens> ExchangeCodeAsync(
        string code, string clientId, string clientSecret, string redirectUri, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        });
        using var res = await http.PostAsync("token", content, ct);
        await EnsureSuccessWithBodyAsync(res, ct);
        return await ReadTokens(res, ct);
    }

    public async Task<GoogleTokens> RefreshAsync(
        string refreshToken, string clientId, string clientSecret, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
        });
        using var res = await http.PostAsync("token", content, ct);
        await EnsureSuccessWithBodyAsync(res, ct);
        return await ReadTokens(res, ct);
    }

    public async Task<GoogleLoginIdentity> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(request, ct);
        await EnsureSuccessWithBodyAsync(res, ct);
        var body = await res.Content.ReadFromJsonAsync<UserInfoResponse>(ct)
            ?? throw new InvalidOperationException("Empty userinfo response.");
        if (string.IsNullOrWhiteSpace(body.Sub) || string.IsNullOrWhiteSpace(body.Email))
            throw new InvalidOperationException("Google userinfo response is missing sub or email.");
        return new GoogleLoginIdentity(body.Sub, body.Email.Trim(), body.EmailVerified, body.Name ?? "");
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Google token endpoint returned {(int)res.StatusCode}: {body}", null, res.StatusCode);
    }

    private static async Task<GoogleTokens> ReadTokens(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("Empty token response.");
        if (body.ExpiresIn <= 0)
            throw new InvalidOperationException($"Token response has invalid expires_in ({body.ExpiresIn}).");
        return new GoogleTokens(body.AccessToken, body.RefreshToken,
            DateTime.UtcNow.AddSeconds(body.ExpiresIn - 60));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("email_verified")] bool EmailVerified,
        [property: JsonPropertyName("name")] string? Name);
}
