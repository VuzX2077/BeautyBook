using System.Net;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class SupabaseStorageAuthenticationTests
{
    public const string Secret = "sb_secret_test_only_fake_value";
    public const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.c3ludGhldGlj";
    private const string Key = "verification/13ff36b7f79d4a0a8c12d7c8fe37ec25/1f2704faa6c5961590112899a6bdc5a0.jpg";
    private static IConfiguration Config(string key) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Supabase:Url"] = "https://storage.example.test", ["Supabase:ServiceRoleKey"] = key,
        ["Supabase:StorageBucket"] = "images", ["Supabase:VerificationBucket"] = "verification-private",
        ["Supabase:FinancialBucket"] = "financial-private" }).Build();

    private sealed class Handler(string key, bool bearer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("storage.example.test", request.RequestUri!.Host);
            Paths.Add(request.RequestUri.AbsolutePath);
            Assert.Equal(key, request.Headers.GetValues("apikey").Single());
            if (bearer) { Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal(key, request.Headers.Authorization?.Parameter); }
            else Assert.False(request.Headers.Contains("Authorization"));
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.StartsWith("/storage/v1/object/images/")) {
                Assert.Equal("false", request.Headers.GetValues("x-upsert").Single());
                Assert.Equal("image/jpeg", request.Content!.Headers.ContentType!.MediaType);
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = request.RequestUri.AbsolutePath.Contains("/bucket/")
                ? new StringContent("{\"public\":false}") : new ByteArrayContent([1, 2, 3]) });
        }
    }

    [Theory]
    [InlineData(Secret, false)]
    [InlineData(Jwt, true)]
    public async Task PrivateDownload_preserves_headers_exact_path_and_bytes(string key, bool bearer)
    {
        var handler = new Handler(key, bearer);
        Assert.Equal(new byte[] { 1, 2, 3 }, await new SupabaseVerificationStorage(new(handler), Config(key)).DownloadAsync(Key));
        Assert.Equal("/storage/v1/object/authenticated/verification-private/" + Key, Assert.Single(handler.Paths));
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(500)] [InlineData(503)]
    public async Task PrivateDownload_errors_fail_without_missing_conversion(int status)
    {
        var handler = new Handler(Secret, false, (HttpStatusCode)status);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(new(handler), Config(Secret)).DownloadAsync(Key));
        Assert.Contains($"({status})", error.Message); Assert.DoesNotContain(Secret, error.ToString());
    }

    [Fact]
    public async Task PrivateDownload_only_404_is_missing()
    {
        var handler = new Handler(Secret, false, HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<StorageObjectMissingException>(() => new SupabaseVerificationStorage(new(handler), Config(Secret)).DownloadAsync(Key));
    }

    [Theory]
    [InlineData(Secret, false)] [InlineData(Jwt, true)]
    public async Task PublicUpload_preserves_headers_endpoint_content_type_and_no_upsert(string key, bool bearer)
    {
        var handler = new Handler(key, bearer);
        var url = await new SupabaseImageStorage(new(handler), Config(key)).UploadPublicImageAsync(new MemoryStream([1]), "image/jpeg", ".jpg");
        Assert.StartsWith("/storage/v1/object/images/uploads/", Assert.Single(handler.Paths));
        Assert.Equal("https://storage.example.test" + handler.Paths[0].Replace("/object/images/", "/object/public/images/"), url);
    }

    [Theory]
    [InlineData(Secret, false)] [InlineData(Jwt, true)]
    public async Task FinancialDownload_delegates_authentication(string key, bool bearer)
    {
        var handler = new Handler(key, bearer);
        Assert.Equal(new byte[] { 1, 2, 3 }, await new SupabaseFinancialStorage(new(handler), Config(key)).DownloadAsync("financial/test.jpg"));
        Assert.Equal(new[] { "/storage/v1/bucket/financial-private", "/storage/v1/object/authenticated/financial-private/financial/test.jpg" }, handler.Paths);
    }

    [Theory]
    [InlineData("garbage")] [InlineData("sb_secret_")] [InlineData("sb_secret_bad\r\nvalue")] [InlineData("a..b")]
    public async Task Unknown_format_fails_before_network_without_credential_in_error(string key)
    {
        var handler = new Handler(key, false);
        var privateError = await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(new(handler), Config(key)).DownloadAsync(Key));
        var publicError = await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseImageStorage(new(handler), Config(key)).UploadPublicImageAsync(new MemoryStream([1]), "image/jpeg", ".jpg"));
        Assert.Empty(handler.Paths); Assert.DoesNotContain(key, privateError.Message); Assert.DoesNotContain(key, publicError.Message);
    }
}
