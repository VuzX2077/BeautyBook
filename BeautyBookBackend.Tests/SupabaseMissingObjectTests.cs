using System.Net;
using System.Text;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class SupabaseMissingObjectTests
{
    private const string Key = "verification/test/front.jpg";
    private const string Missing = "{\"statusCode\":\"404\",\"code\":\"NoSuchKey\",\"error\":\"not_found\",\"message\":\"Object not found\"}";
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Supabase:Url"] = "https://storage.example.test", ["Supabase:ServiceRoleKey"] = SupabaseStorageAuthenticationTests.Secret,
        ["Supabase:VerificationBucket"] = "verification-private", ["Supabase:StorageBucket"] = "images", ["Supabase:FinancialBucket"] = "financial-private" }).Build();
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; var response = respond(request); response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
    private static HttpResponseMessage Json(int status, string body) => new((HttpStatusCode)status) {
        Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(Missing)]
    [InlineData("{\"code\":\"NoSuchKey\"}")]
    [InlineData("{\"code\":\"not_found\"}")]
    [InlineData("{\"httpStatusCode\":404,\"code\":\"not_found\"}")]
    [InlineData("{\"statusCode\":404,\"error\":\"not_found\"}")]
    [InlineData("{\"statusCode\":\"404\",\"code\":\"NoSuchKey\",\"error\":\"NoSuchKey\"}")]
    [InlineData("{\"statusCode\":\"404\",\"code\":\"NoSuchKey\",\"error\":\"Not found\"}")]
    public async Task Recognized_400_private_get_is_missing(string body)
    {
        using var http = new HttpClient(new Handler(_ => Json(400, body)));
        await Assert.ThrowsAsync<StorageObjectMissingException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
    }

    [Theory]
    [InlineData("")] [InlineData("{")] [InlineData("[]")] [InlineData("null")]
    [InlineData("{\"code\":\"InvalidRequest\",\"message\":\"Object not found\"}")]
    [InlineData("{\"message\":\"Object not found\"}")]
    [InlineData("{\"error\":\"not_found\"}")]
    [InlineData("{\"code\":\"NoSuchBucket\"}")]
    [InlineData("{\"code\":\"not_found\",\"statusCode\":\"403\"}")]
    [InlineData("{\"code\":\"NoSuchKey\",\"error\":\"AccessDenied\"}")]
    [InlineData("{\"code\":\"AccessDenied\",\"error\":\"not_found\",\"statusCode\":\"404\"}")]
    [InlineData("{\"code\":\"not_found\",\"code\":\"not_found\"}")]
    [InlineData("{\"code\":null,\"error\":\"not_found\",\"statusCode\":404}")]
    [InlineData("{\"code\":\"NOT_FOUND\"}")]
    [InlineData("{\"code\":\"not_found\",\"statusCode\":404,\"httpStatusCode\":400}")]
    [InlineData("{\"code\":\"not_found\",\"statusCode\":true}")]
    public async Task Unrecognized_or_ambiguous_400_body_fails_closed(string body)
    {
        using var http = new HttpClient(new Handler(_ => Json(400, body)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        Assert.Equal("Verification storage request failed (400).", error.Message);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(500)] [InlineData(503)]
    public async Task Auth_and_server_errors_never_become_missing(int status)
    {
        using var http = new HttpClient(new Handler(_ => Json(status, Missing)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
    }

    [Fact]
    public async Task Non_json_or_redirected_response_is_not_trusted()
    {
        foreach (var redirect in new[] { false, true })
        {
            using var http = new HttpClient(new Handler(_ => redirect ? new(HttpStatusCode.BadRequest) {
                RequestMessage = new(HttpMethod.Get, "https://other.example.test/storage/v1/object/authenticated/verification-private/" + Key),
                Content = new StringContent(Missing, Encoding.UTF8, "application/json") }
                : new(HttpStatusCode.BadRequest) { Content = new StringContent(Missing) }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        }
    }

    [Fact]
    public async Task Mapping_does_not_apply_to_bucket_upload_sign_delete_or_legacy_public_get()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.Contains("/bucket/")
            ? Json(200, "{\"public\":false}") : Json(400, Missing));
        using var http = new HttpClient(handler); var storage = new SupabaseVerificationStorage(http, Config());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.UploadAsync(Key, [1]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SignAsync(Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DownloadAsync(Key, legacy: true));
        using var bucketHttp = new HttpClient(new Handler(_ => Json(400, Missing)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(bucketHttp, Config()).EnsurePrivateAsync());
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(500)] [InlineData(404)]
    public async Task Financial_delegation_preserves_missing_semantics_and_isolation(int status)
    {
        using var handler = new Handler(request => {
            Assert.Contains("/financial-private", request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/bucket/") ? Json(200, "{\"public\":false}") : Json(status, Missing);
        });
        using var http = new HttpClient(handler);
        var task = new SupabaseFinancialStorage(http, Config()).DownloadAsync("financial/test.jpg");
        if (status is 400 or 404) await Assert.ThrowsAsync<StorageObjectMissingException>(() => task);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class TrackedStream(byte[] bytes, bool cancel = false) : Stream
    {
        public int ReadBytes;
        public bool Disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (cancel) throw new OperationCanceledException(ct);
            var count = Math.Min(buffer.Length, bytes.Length - ReadBytes);
            bytes.AsMemory(ReadBytes, count).CopyTo(buffer); ReadBytes += count;
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Error_body_size_is_bounded_even_without_content_length(bool advertised)
    {
        using var stream = new TrackedStream(Encoding.UTF8.GetBytes(Missing + new string(' ', 8000)));
        using var content = new StreamContent(stream); content.Headers.ContentType = new("application/json");
        if (advertised) content.Headers.ContentLength = 8000;
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.BadRequest) { Content = content }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        Assert.Equal(advertised ? 0 : 4097, stream.ReadBytes); Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Exact_limit_is_accepted_but_truncation_is_not()
    {
        foreach (var body in new[] { Missing.PadRight(4096), Missing[..^1] })
        {
            using var http = new HttpClient(new Handler(_ => Json(400, body)));
            var task = new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key);
            if (body.Length == 4096) await Assert.ThrowsAsync<StorageObjectMissingException>(() => task);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        }
    }

    [Fact]
    public async Task Cancellation_disposes_response_and_is_preserved()
    {
        using var stream = new TrackedStream([], cancel: true);
        using var content = new StreamContent(stream); content.Headers.ContentType = new("application/json");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.BadRequest) { Content = content }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Existing_object_returns_unchanged_bytes_without_upload()
    {
        using var handler = new Handler(request => {
            Assert.Equal(HttpMethod.Get, request.Method);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) };
        });
        using var http = new HttpClient(handler);
        Assert.Equal(new byte[] { 9, 8, 7 }, await new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Provider_body_and_credentials_never_appear_in_errors()
    {
        var body = "{\"code\":\"InvalidRequest\",\"message\":\"" + SupabaseStorageAuthenticationTests.Secret + "\"}";
        using var http = new HttpClient(new Handler(_ => Json(400, body)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SupabaseVerificationStorage(http, Config()).DownloadAsync(Key));
        Assert.DoesNotContain(SupabaseStorageAuthenticationTests.Secret, error.ToString());
        Assert.DoesNotContain(body, error.ToString());
        Assert.Null(error.InnerException);
    }
}
