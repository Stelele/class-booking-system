using Infrastructure.Notifications;
using Lib.Net.Http.WebPush.Authentication;
using Xunit;

public class VapidKeyFormatTests
{
    [Fact]
    public void Generated_keys_have_the_lengths_rfc8292_requires()
    {
        var keys = VapidKeys.Generate();
        // Uncompressed P-256 point: 0x04 || X || Y
        Assert.Equal(65, FromBase64Url(keys.PublicKey).Length);
        // Raw 32-byte scalar
        Assert.Equal(32, FromBase64Url(keys.PrivateKey).Length);
    }

    [Fact]
    public void Public_key_starts_with_the_uncompressed_point_marker()
    {
        // A SPKI blob would begin 0x30 here; RFC 8292 wants the raw point.
        Assert.Equal(0x04, FromBase64Url(VapidKeys.Generate().PublicKey)[0]);
    }

    [Fact]
    public void Keys_are_base64url_without_padding()
    {
        var keys = VapidKeys.Generate();
        Assert.DoesNotContain('=', keys.PublicKey);
        Assert.DoesNotContain('=', keys.PrivateKey);
        Assert.DoesNotContain('+', keys.PublicKey);
        Assert.DoesNotContain('/', keys.PublicKey);
    }

    [Fact]
    public void Generated_keys_are_accepted_by_the_push_library()
    {
        // VapidAuthentication validates key lengths and throws otherwise, so
        // constructing it is the round-trip proof the format is right.
        var keys = VapidKeys.Generate();
        var auth = new VapidAuthentication(keys.PublicKey, keys.PrivateKey)
        {
            Subject = "mailto:test@example.com",
        };
        Assert.NotNull(auth);
    }

    [Fact]
    public void Each_generation_is_a_distinct_key_pair()
    {
        Assert.NotEqual(VapidKeys.Generate().PublicKey, VapidKeys.Generate().PublicKey);
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
