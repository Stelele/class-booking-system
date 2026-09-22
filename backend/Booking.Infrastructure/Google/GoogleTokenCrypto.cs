using System.Security.Cryptography;

namespace Booking.Infrastructure.Google;

/// AES-GCM with random 12-byte nonce prepended (nonce|ciphertext|tag), base64.
/// Key: 32 bytes from base64 env Google:TokenKey.
public sealed class GoogleTokenCrypto(byte[] key)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Decrypt(string payload)
    {
        var all = Convert.FromBase64String(payload);
        var plain = new byte[all.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(all[..NonceSize], all[NonceSize..^TagSize], all[^TagSize..], plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}