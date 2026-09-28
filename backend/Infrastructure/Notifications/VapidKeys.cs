using System.Security.Cryptography;

namespace Infrastructure.Notifications;

/// <summary>
/// Generates VAPID P-256 key pairs in the exact shape RFC 8292 requires:
/// the public key is the raw 65-byte uncompressed point (0x04 || X || Y),
/// not a SPKI blob, and the private key is the raw 32-byte scalar. Both are
/// base64url. Dev/test only — keys are generated once and stored as secrets.
/// </summary>
public static class VapidKeys
{
    public sealed record KeyPair(string PublicKey, string PrivateKey);

    public static KeyPair Generate()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        // includePrivateParameters: true — we need both halves.
        var p = ecdh.ExportParameters(includePrivateParameters: true);

        var publicKey = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
        publicKey[0] = 0x04;
        p.Q.X.CopyTo(publicKey, 1);
        p.Q.Y.CopyTo(publicKey, 1 + p.Q.X.Length);

        return new KeyPair(Base64Url(publicKey), Base64Url(p.D!));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
