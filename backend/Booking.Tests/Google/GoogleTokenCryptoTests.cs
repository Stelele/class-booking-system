using System.Security.Cryptography;
using Booking.Infrastructure.Google;
using Xunit;

public class GoogleTokenCryptoTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

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

    [Fact]
    public void Wire_format_is_nonce_ciphertext_tag()
    {
        var crypto = new GoogleTokenCrypto(Key());
        var raw = Convert.FromBase64String(crypto.Encrypt("hello"));
        Assert.Equal(12 + 5 + 16, raw.Length);
    }

    [Fact]
    public void Empty_plaintext_roundtrips()
    {
        var crypto = new GoogleTokenCrypto(Key());
        Assert.Equal("", crypto.Decrypt(crypto.Encrypt("")));
    }

    [Fact]
    public void Tampered_ciphertext_fails_decrypt()
    {
        var crypto = new GoogleTokenCrypto(Key());
        var raw = Convert.FromBase64String(crypto.Encrypt("hello"));
        raw[15] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Truncated_payload_throws_cryptographic_not_overflow()
    {
        var crypto = new GoogleTokenCrypto(Key());
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt(Convert.ToBase64String(new byte[10])));
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt(""));
    }

    [Fact]
    public void Invalid_base64_throws_cryptographic()
    {
        var crypto = new GoogleTokenCrypto(Key());
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt("!!!not-base64!!!"));
    }

    [Fact]
    public void Null_inputs_throw_argument_null()
    {
        var crypto = new GoogleTokenCrypto(Key());
        Assert.Throws<ArgumentNullException>(() => crypto.Encrypt(null!));
        Assert.Throws<ArgumentNullException>(() => crypto.Decrypt(null!));
    }

    [Fact]
    public void Non_32_byte_key_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new GoogleTokenCrypto(new byte[16]));
        Assert.Throws<ArgumentException>(() => new GoogleTokenCrypto(new byte[10]));
        Assert.Throws<ArgumentNullException>(() => new GoogleTokenCrypto(null!));
    }
}