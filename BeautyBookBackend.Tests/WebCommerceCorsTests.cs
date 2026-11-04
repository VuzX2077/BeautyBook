using System.Net;
using BeautyBookBackend.Services.Monetization;

namespace BeautyBookBackend.Tests;

public sealed class WebCommerceCorsTests
{
    [Fact]
    public async Task Preflight_allows_only_exact_website_origin_and_bearer_headers_without_cookies()
    {
        await using var factory = new PrivateMediaHttpTests.LocalFactory("Host=127.0.0.1;Port=55439;Username=postgres;Database=unused-cors-test");
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/web/mua/login", "/api/web/mua/orders", "/api/web/mua/me", "/api/web/mua/logout", "/api/Booking" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, path);
            request.Headers.Add("Origin", MuaWebAuthentication.AllowedOrigin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(MuaWebAuthentication.AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
            Assert.Contains("authorization", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        }
        foreach (var origin in new[] { "https://evil.test", "https://bbookmakeup.com.evil.test", "https://www.bbookmakeup.com", "http://bbookmakeup.com", "null" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/web/mua/orders");
            request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            using var response = await client.SendAsync(request);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
        using var blocked = new HttpRequestMessage(HttpMethod.Post, "/api/web/mua/login");
        blocked.Headers.Add("Origin", "https://evil.test"); blocked.Content = System.Net.Http.Json.JsonContent.Create(new { email = "test@example.test", password = "password" });
        using var denied = await client.SendAsync(blocked);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
