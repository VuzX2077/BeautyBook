using System.Net;
using System.Security.Cryptography;
using System.Text;
using BeautyBook.StorageVerification;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class StorageVerifierTests
{
    private const string Origin = "https://storage.example.test";
    private const string Secret = "sb_secret_test_only_fake_value";
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.c3ludGhldGlj";
    private static IConfiguration Config(string key = Secret, string url = Origin, string bucket = StorageVerifier.Bucket) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Supabase:Url"] = url, ["Supabase:ServiceRoleKey"] = key,
            ["Supabase:VerificationBucket"] = bucket, ["Supabase:StorageBucket"] = "images" }).Build();
    private static readonly byte[][] Bytes = [ [1, 2, 3], [4, 5], [6] ];
    private static IReadOnlyList<ExpectedObject> Synthetic => StorageVerifier.Manifest.Select((o, i) =>
        o with { Size = Bytes[i].Length, Hash = Convert.ToHexString(SHA256.HashData(Bytes[i])) }).ToArray();
    private sealed class Fake(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = respond(request, ++Calls);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
    private static HttpResponseMessage Json(int status, string body) => new((HttpStatusCode)status) {
        Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Success(int call) => call == 1 ? Json(200, "{\"public\":false}")
        : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes[call - 2]) };
    private static Task<int> Run(Fake fake, StringWriter output, IConfiguration? config = null) =>
        StorageVerifier.RunCoreAsync(config ?? Config(), fake, output, Synthetic);

    [Theory]
    [InlineData(Secret, false)] [InlineData(Jwt, true)]
    public async Task All_three_matches_require_exact_four_GETs_using_real_adapter_auth(string key, bool bearer)
    {
        using var fake = new Fake((request, call) => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal(key, request.Headers.GetValues("apikey").Single());
            Assert.Equal(bearer, request.Headers.Contains("Authorization"));
            if (bearer) Assert.Equal(key, request.Headers.Authorization!.Parameter);
            Assert.Equal(Origin + (call == 1 ? "/storage/v1/bucket/verification-private"
                : "/storage/v1/object/authenticated/verification-private/" + Synthetic[call - 2].Key), request.RequestUri!.AbsoluteUri);
            return Success(call);
        });
        using var output = new StringWriter();
        Assert.Equal(0, await Run(fake, output, Config(key)));
        Assert.Equal(4, fake.Calls);
        Assert.Contains("PASS_ALL_3_OBJECTS", output.ToString());
        Assert.Equal(3, output.ToString().Split("object result: MATCH").Length - 1);
        Assert.DoesNotContain(key, output.ToString());
    }

    [Fact]
    public void Manifest_is_fixed_to_approved_production_objects()
    {
        Assert.Equal(new[] { 181721, 192615, 62945 }, StorageVerifier.Manifest.Select(o => o.Size));
        Assert.Equal(new[] {
            "403EE4B6525CD974F18CB0F1FB82D33537825F4A05A65E45FFE2D788BD0A143B",
            "4AC78D9EAA316BBE3D851FE0E9D1F90FB69C939C1F0BD240318AF9318D5A5707",
            "E2EB3E4FA08CF514438182C1CCAF2D586FC1DF634EC098EE96F7F9C9D2089D1F" }, StorageVerifier.Manifest.Select(o => o.Hash));
        Assert.All(StorageVerifier.Manifest, o => Assert.StartsWith(StorageVerifier.Prefix, o.Key));
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)]
    [InlineData(500)] [InlineData(503)] [InlineData(302)] [InlineData(206)] [InlineData(204)]
    public async Task Unexpected_object_status_never_succeeds_or_repairs(int status)
    {
        using var fake = new Fake((_, call) => call == 1 ? Success(call) : Json(status,
            "{\"code\":\"not_found\",\"message\":\"Object not found\"}"));
        using var output = new StringWriter();
        Assert.Equal(2, await Run(fake, output));
        Assert.Equal(2, fake.Calls);
        Assert.DoesNotContain("PASS_ALL", output.ToString());
    }

    [Theory]
    [InlineData("{\"public\":true}")] [InlineData("{}")] [InlineData("not-json")]
    [InlineData("{\"public\":true,\"public\":false}")]
    public async Task Bucket_privacy_failure_stops_before_any_object(string body)
    {
        using var fake = new Fake((_, _) => Json(200, body));
        using var output = new StringWriter();
        Assert.Equal(2, await Run(fake, output));
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Wrong_size_or_same_size_wrong_hash_stops_at_first_object(bool sameSize)
    {
        using var fake = new Fake((_, call) => call == 1 ? Success(call)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(sameSize ? [3, 2, 1] : [1]) });
        using var output = new StringWriter();
        Assert.Equal(2, await Run(fake, output));
        Assert.Contains("FAIL_CONTENT_MISMATCH", output.ToString());
        Assert.Equal(2, fake.Calls);
    }

    // Unknown Content-Length must still be bounded by streaming, not just headers.
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int ReadCount;
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var read = await base.ReadAsync(buffer, ct); ReadCount += read; return read; }
    }
    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    public async Task Bucket_object_and_error_bodies_are_bounded(int kind, bool knownLength)
    {
        var limit = kind == 1 ? Synthetic[0].Size : 4096;
        using var stream = new CountingStream(new byte[limit + 100]);
        using var fake = new Fake((_, call) => {
            if (kind != 0 && call == 1) return Success(call);
            var content = new StreamContent(stream);
            if (knownLength) content.Headers.ContentLength = limit + 100;
            return new((HttpStatusCode)(kind == 2 ? 400 : 200)) { Content = content };
        });
        using var output = new StringWriter();
        Assert.Equal(2, await Run(fake, output));
        Assert.True(stream.ReadCount <= limit + 1);
        Assert.Equal(knownLength ? 0 : limit + 1, stream.ReadCount);
        Assert.Equal(kind == 0 ? 1 : 2, fake.Calls);
    }

    [Fact]
    public async Task Secrets_raw_body_and_transport_details_are_not_printed()
    {
        foreach (var transportFailure in new[] { false, true })
        {
            using var fake = new Fake((_, call) => call == 1 ? Success(call)
                : transportFailure ? throw new HttpRequestException(Secret + Jwt)
                : Json(400, "{\"code\":\"" + Secret + "\",\"message\":\"" + Jwt + "\",\"token\":\"raw-body-marker\"}"));
            using var output = new StringWriter();
            Assert.Equal(2, await Run(fake, output));
            Assert.DoesNotContain(Secret, output.ToString());
            Assert.DoesNotContain(Jwt, output.ToString());
            Assert.DoesNotContain("raw-body-marker", output.ToString());
        }
    }

    [Theory]
    [InlineData("POST")] [InlineData("PUT")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Guard_blocks_all_mutation_methods_before_transport(string method)
    {
        using var fake = new Fake((_, call) => Success(call));
        using var client = new HttpClient(new VerificationRequestGuard(new Uri(Origin), fake, Synthetic));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            new(new HttpMethod(method), Origin + "/storage/v1/bucket/verification-private")));
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData("https://other.example.test/storage/v1/bucket/verification-private")]
    [InlineData(Origin + "/storage/v1/bucket/images")]
    [InlineData(Origin + "/storage/v1/bucket/verification-private?token=x")]
    [InlineData(Origin + "/storage/v1/bucket/verification-private#x")]
    [InlineData(Origin + "/storage/v1/object/authenticated/verification-private/other.jpg")]
    public async Task Guard_denies_other_origins_paths_queries_and_fragments(string uri)
    {
        using var fake = new Fake((_, call) => Success(call));
        using var client = new HttpClient(new VerificationRequestGuard(new Uri(Origin), fake, Synthetic));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(uri));
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Guard_denies_GET_body_and_fifth_request()
    {
        using var fake = new Fake((_, call) => Success(call));
        using var client = new HttpClient(new VerificationRequestGuard(new Uri(Origin), fake, Synthetic));
        var bucketUri = Origin + "/storage/v1/bucket/verification-private";
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new(HttpMethod.Get, bucketUri) { Content = new StringContent("body") }));
        using var bucket = await client.GetAsync(bucketUri);
        foreach (var item in Synthetic)
        { using var response = await client.GetAsync(Origin + "/storage/v1/object/authenticated/verification-private/" + item.Key); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(bucketUri));
        Assert.Equal(4, fake.Calls);
    }

    [Fact]
    public async Task Changed_response_origin_fails_even_with_200()
    {
        using var fake = new Fake((_, call) => {
            var response = Success(call);
            response.RequestMessage = new(HttpMethod.Get, "https://other.example.test/");
            return response;
        });
        using var output = new StringWriter();
        Assert.Equal(2, await Run(fake, output));
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public void Actual_transport_never_follows_redirects_or_uses_cookies_or_proxy()
    {
        using var transport = StorageVerifier.CreateTransport();
        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseProxy);
    }

    [Fact]
    public async Task Invalid_config_is_denied_before_transport()
    {
        using var fake = new Fake((_, call) => Success(call));
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(fake, output, Config(bucket: "images")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(fake, output, Config(url: Origin + "?token=x")));
        Assert.Equal(0, fake.Calls);
    }
}
