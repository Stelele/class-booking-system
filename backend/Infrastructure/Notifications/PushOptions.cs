namespace Infrastructure.Notifications;

public sealed class PushOptions
{
    /// <summary>Base64url, 65-byte uncompressed P-256 point. Safe to expose.</summary>
    public string VapidPublicKey { get; set; } = "";

    /// <summary>Base64url, 32-byte P-256 scalar. Server secret.</summary>
    public string VapidPrivateKey { get; set; } = "";

    /// <summary>mailto: or https: contact the push service can reach.</summary>
    public string VapidSubject { get; set; } = "";
}
