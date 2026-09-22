using System.Security.Cryptography;
using Booking.Infrastructure.Google;
using Xunit;

public class GoogleTokenCryptoTests
{
    private static byte[] Key() => Convert.FromBase64String(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public void Roundtrip_returns_original()
    {
        var crypto = new GoogleTokenCrypto(Key());
        var cipher = crypto.Encrypt("refresh-token-abc");
        Assert.Equal("refresh-token-abc", crypto.Decrypt(cipher));
    }

    [Fact]
    public void Same_plaintext_encrypts_differently_each_time()
    {
        var crypto = new GoogleTokenCrypto(Key());
        Assert.NotEqual(crypto.Encrypt("x"), crypto.Encrypt("x"));
    }

    [Fact]
    public void Wrong_key_fails_decrypt()
    {
        var crypto = new GoogleTokenCrypto(Key());
        var cipher = crypto.Encrypt("x");
        Assert.ThrowsAny<CryptographicException>(() => new GoogleTokenCrypto(Key()).Decrypt(cipher));
    }
}