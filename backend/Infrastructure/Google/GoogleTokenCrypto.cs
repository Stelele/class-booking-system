using System.Security.Cryptography;

namespace Infrastructure.Google;

/// AES-GCM with random 12-byte nonce prepended (nonce|ciphertext|tag), base64.
/// Key: exactly 32 bytes from base64 env Google:TokenKey (AES-256; no associated data).
/// All failure modes surface as ArgumentException (bad input) or
/// CryptographicException (bad key/payload) — never OverflowException/FormatException.
public sealed class GoogleTokenCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MinPayloadSize = NonceSize + TagSize;
    private readonly byte[] _key;

    public GoogleTokenCrypto(byte[] key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (key.Length != 32)
            throw new ArgumentException("Key must be exactly 32 bytes (AES-256).", nameof(key));
        _key = (byte[])key.Clone();
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Decrypt(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        byte[] all;
        try { all = Convert.FromBase64String(payload); }
        catch (FormatException ex) { throw new CryptographicException("Payload is not valid base64.", ex); }
        if (all.Length < MinPayloadSize)
            throw new CryptographicException($"Payload too short ({all.Length} bytes, minimum {MinPayloadSize}).");
        var plain = new byte[all.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(all[..NonceSize], all[NonceSize..^TagSize], all[^TagSize..], plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}