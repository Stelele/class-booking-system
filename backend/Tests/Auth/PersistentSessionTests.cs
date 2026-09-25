using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Tests.Auth;

[Collection("Api")]
public class PersistentSessionTests
{
    [Fact]
    public async Task Cookie_auth_survives_host_restart_when_keys_are_persisted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "booking.db");
        var keyDir = Path.Combine(tempDir, "data-protection-keys");
        string setCookie;
        try
        {
            using (var first = new TempDbApiFactory(dbPath))
            {
                var client = first.CreateClient();
                var request = await client.PostAsJsonAsync("/api/auth/request-code",
                    new { email = "teacher@example.com" });
                Assert.Equal(HttpStatusCode.OK, request.StatusCode);

                var verify = await client.PostAsJsonAsync("/api/auth/verify",
                    new { email = "teacher@example.com", code = ApiFactory.LastCode });
                Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
                Assert.True(File.Exists(dbPath));
                Assert.True(Directory.Exists(keyDir));
                Assert.NotEmpty(Directory.GetFiles(keyDir));
                Assert.True(verify.Headers.TryGetValues("Set-Cookie", out var setCookies));
                var cookiePairs = setCookies!
                    .Select(header => header.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                    .Where(pair => !string.IsNullOrWhiteSpace(pair))
                    .Select(pair => pair!.Trim())
                    .ToArray();
                Assert.NotEmpty(cookiePairs);
                setCookie = string.Join("; ", cookiePairs);
            }

            using (var second = new TempDbApiFactory(dbPath))
            {
                var client = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                me.Headers.Add("Cookie", setCookie);
                var response = await client.SendAsync(me);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            try
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class TempDbApiFactory : ApiFactory
    {
        private readonly string _dbPath;
        public TempDbApiFactory(string dbPath) => _dbPath = dbPath;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("App:DbPath", _dbPath);
        }
    }
}
