using System.Net;
using BeautyBook.StorageDiagnostics;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class ReadOnlyStorageDiagnosticTests
{
    private const string Origin = "https://storage.example.test";
    private const string Secret = "sb_secret_test_only_fake_value";
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.c3ludGhldGlj";
    private static IConfiguration Config(string key = Secret, string bucket = ReadOnlyStorageDiagnostic.Bucket) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Supabase:Url"] = Origin, ["Supabase:ServiceRoleKey"] = key,
            ["Supabase:VerificationBucket"] = bucket, ["Supabase:StorageBucket"] = "images" }).Build();

    private sealed class Fake(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request, ++Calls));
    }
    private static HttpResponseMessage Response(int status, string body) => new((HttpStatusCode)status) { Content = new StringContent(body) };

    [Fact]
    public void Production_transport_disables_redirects_cookies_and_proxy()
    {
        using var transport = ReadOnlyStorageDiagnostic.CreateTransport();
        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseProxy);
    }

    [Fact]
    public async Task Transport_exception_details_are_not_exposed()
    {
        using var fake = new Fake((_, _) => throw new HttpRequestException(Secret + Jwt));
        using var output = new StringWriter();
        Assert.Equal(2, await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output));
        Assert.Contains("bucket HTTP status: unavailable", output.ToString());
        Assert.DoesNotContain(Secret, output.ToString());
        Assert.DoesNotContain(Jwt, output.ToString());
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(Secret, false)] [InlineData(Jwt, true)]
    public async Task Actual_adapter_auth_and_exact_two_gets_are_reused(string key, bool bearer)
    {
        using var fake = new Fake((request, call) => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal(key, request.Headers.GetValues("apikey").Single());
            if (bearer) { Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal(key, request.Headers.Authorization?.Parameter); }
            else Assert.False(request.Headers.Contains("Authorization"));
            Assert.Equal(Origin + (call == 1 ? "/storage/v1/bucket/verification-private"
                : "/storage/v1/object/authenticated/verification-private/" + ReadOnlyStorageDiagnostic.ObjectKey), request.RequestUri!.AbsoluteUri);
            return Response(call == 1 ? 200 : 404, call == 1 ? "{\"public\":false}" : "{\"message\":\"Object not found\"}");
        });
        using var output = new StringWriter();
        Assert.Equal(0, await ReadOnlyStorageDiagnostic.RunAsync(Config(key), fake, output));
        Assert.Equal(2, fake.Calls);
        Assert.Contains("OBJECT_MISSING", output.ToString());
        Assert.DoesNotContain(key, output.ToString());
    }

    [Theory]
    [InlineData(400, "STOP_BAD_REQUEST")] [InlineData(401, "STOP_AUTH_PERMISSION_FAILURE")]
    [InlineData(403, "STOP_AUTH_PERMISSION_FAILURE")] [InlineData(500, "STOP_PROVIDER_SERVER_FAILURE")]
    [InlineData(503, "STOP_PROVIDER_SERVER_FAILURE")] [InlineData(302, "STOP_UNEXPECTED")]
    [InlineData(206, "STOP_UNEXPECTED")]
    public async Task Non_200_non_404_object_responses_stop(int status, string result)
    {
        using var fake = new Fake((_, call) => Response(call == 1 ? 200 : status, "{\"public\":false}"));
        using var output = new StringWriter();
        Assert.Equal(2, await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output));
        Assert.Equal(2, fake.Calls);
        Assert.Contains(result, output.ToString());
        Assert.DoesNotContain("OBJECT_MISSING", output.ToString());
    }

    [Theory]
    [InlineData(200, "{\"public\":true}")] [InlineData(200, "{}")]
    [InlineData(200, "not json")] [InlineData(404, "{}")] [InlineData(401, "{}")]
    [InlineData(302, "{}")] [InlineData(500, "{}")]
    public async Task Bucket_failure_never_requests_object(int status, string body)
    {
        using var fake = new Fake((_, _) => Response(status, body));
        using var output = new StringWriter();
        Assert.Equal(2, await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output));
        Assert.Equal(1, fake.Calls);
        Assert.DoesNotContain("object key:", output.ToString());
    }

    [Fact]
    public async Task Successful_download_hashes_in_memory_and_stops_on_mismatch()
    {
        using var fake = new Fake((_, call) => call == 1 ? Response(200, "{\"public\":false}")
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var output = new StringWriter();
        Assert.Equal(2, await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output));
        Assert.Contains("size: 3", output.ToString());
        Assert.Contains("SHA256: 039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81", output.ToString());
        Assert.Contains("STOP_CONTENT_MISMATCH", output.ToString());
    }

    [Theory]
    [InlineData("{\"code\":\"NoSuchKey\",\"message\":\"Object not found\"}", "NoSuchKey", "Object not found")]
    [InlineData("{\"code\":\"sb_secret_test_only_fake_value\",\"message\":\"Bearer secret https://example.test/?token=secret\"}", "BODY_REDACTED", "BODY_REDACTED")]
    [InlineData("<html>sb_secret_test_only_fake_value</html>", "BODY_REDACTED", "BODY_REDACTED")]
    [InlineData("{\"code\":\"AccessDenied\",\"message\":\"eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.c3ludGhldGlj\"}", "AccessDenied", "BODY_REDACTED")]
    public async Task Error_body_is_strictly_allowlisted(string body, string code, string message)
    {
        using var fake = new Fake((_, call) => Response(call == 1 ? 200 : 400, call == 1 ? "{\"public\":false}" : body));
        using var output = new StringWriter();
        await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output);
        Assert.Contains("Supabase error code: " + code, output.ToString());
        Assert.Contains("Supabase error message: " + message, output.ToString());
        Assert.DoesNotContain(Secret, output.ToString());
        Assert.DoesNotContain(Jwt, output.ToString());
        Assert.DoesNotContain("token=", output.ToString());
    }

    [Fact]
    public async Task Oversized_error_body_is_redacted()
    {
        using var fake = new Fake((_, call) => Response(call == 1 ? 200 : 400,
            call == 1 ? "{\"public\":false}" : new string('x', 5000) + Secret));
        using var output = new StringWriter();
        await ReadOnlyStorageDiagnostic.RunAsync(Config(), fake, output);
        Assert.Contains("BODY_REDACTED", output.ToString());
        Assert.DoesNotContain(Secret, output.ToString());
    }

    [Theory]
    [InlineData("POST", "/storage/v1/bucket/verification-private")]
    [InlineData("PUT", "/storage/v1/bucket/verification-private")]
    [InlineData("PATCH", "/storage/v1/bucket/verification-private")]
    [InlineData("DELETE", "/storage/v1/bucket/verification-private")]
    [InlineData("GET", "/storage/v1/bucket/images")]
    [InlineData("GET", "/storage/v1/bucket/verification-private?token=x")]
    [InlineData("GET", "/storage/v1/object/authenticated/verification-private/other.jpg")]
    public async Task Request_guard_blocks_mutations_and_wrong_paths(string method, string path)
    {
        using var fake = new Fake((_, _) => Response(200, "{}"));
        using var client = new HttpClient(new ReadOnlyStorageHandler(new Uri(Origin), fake));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new(new HttpMethod(method), Origin + path)));
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Request_guard_blocks_wrong_origin_body_and_third_request()
    {
        using var fake = new Fake((_, _) => Response(200, "{\"public\":false}"));
        using var client = new HttpClient(new ReadOnlyStorageHandler(new Uri(Origin), fake));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://other.example.test/storage/v1/bucket/verification-private"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new(HttpMethod.Get, Origin + "/storage/v1/bucket/verification-private") { Content = new StringContent("body") }));
        using var first = await client.GetAsync(Origin + "/storage/v1/bucket/verification-private");
        using var second = await client.GetAsync(Origin + "/storage/v1/object/authenticated/verification-private/" + ReadOnlyStorageDiagnostic.ObjectKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(Origin + "/storage/v1/object/authenticated/verification-private/" + ReadOnlyStorageDiagnostic.ObjectKey));
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task Wrong_bucket_is_rejected_before_network()
    {
        using var fake = new Fake((_, _) => throw new Exception("Must not call"));
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadOnlyStorageDiagnostic.RunAsync(Config(bucket: "images"), fake, output));
        Assert.Equal(0, fake.Calls);
    }
}
