using System.Security.Cryptography;
using Infrastructure.Notifications;

namespace Tests;

/// <summary>
/// Valid Web Push subscription key material, generated per call rather than
/// pasted in as a literal.
/// </summary>
/// <remarks>
/// A push subscription carries a <c>p256dh</c> key and an <c>auth</c> secret.
/// <c>p256dh</c> is public by design — the browser hands it to the push
/// service in the clear — and <c>auth</c> here is per-test. Neither is a
/// credential, but both are long base64url strings, which secret scanners flag.
/// Generating them keeps the tests working with real, correctly shaped key
/// material (the push library genuinely encrypts with them) without a
/// high-entropy literal in the repository.
/// </remarks>
internal static class PushTestKeys
{
    /// <summary>65-byte uncompressed P-256 point — the same shape as a VAPID public key.</summary>
    public static string P256Dh() => VapidKeys.Generate().PublicKey;

    /// <summary>16-byte shared secret, per RFC 8291.</summary>
    public static string Auth() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
