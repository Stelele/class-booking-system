namespace Application.Abstractions;

public sealed class GoogleOAuthSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}