using System.Net;
using System.Security.Claims;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace BeautyBookBackend.Tests;

public sealed class VerificationMediaSafetyTests
{
    [Fact]
    public void InvalidEncodingHasActionableError()
    {
        var error = Assert.Throws<ArgumentException>(() => VerificationImage.Normalize("not an image"u8.ToArray()));
        Assert.Contains("định dạng", error.Message);
        Assert.DoesNotContain("megapixel", error.Message);
    }

    [Fact]
    public void OversizedImageIsRejectedBeforePixelDecoding()
    {
        using var bitmap = new SKBitmap(5000, 5000);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var error = Assert.Throws<ArgumentException>(() => VerificationImage.Normalize(encoded.ToArray()));
        Assert.Contains("24 megapixel", error.Message);
    }
    [Fact]
    public void ReencodingRejectsFakeImagesAndRemovesTrailingMetadata()
    {
        Assert.Throws<ArgumentException>(() => VerificationImage.Normalize("not an image"u8.ToArray()));
        using var bitmap = new SKBitmap(4, 3);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var input = encoded.ToArray().Concat("private-gps-marker"u8.ToArray()).ToArray();
        var normalized = VerificationImage.Normalize(input);
        Assert.DoesNotContain("private-gps-marker", System.Text.Encoding.UTF8.GetString(normalized));
        using var decoded = SKBitmap.Decode(normalized);
        Assert.Equal(4, decoded.Width);
        Assert.Equal(3, decoded.Height);
    }
    [Fact]
    public async Task LegacyPublicUploadWithoutPurposeIsRejectedBeforeStorage()
    {
        var storage = new NoPublicStorage();
        var controller = new UploadController(storage);
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "cccd.jpg");
        Assert.IsType<BadRequestObjectResult>(await controller.UploadImage(file, null));
        Assert.False(storage.Called);
    }
    private sealed class NoPublicStorage : IImageStorage
    {
        public bool Called { get; private set; }
        public Task<string> UploadPublicImageAsync(Stream content, string contentType, string extension, CancellationToken cancellationToken = default)
        { Called = true; return Task.FromResult("https://public.test/image.jpg"); }
    }
    [Fact]
    public async Task StorageRefusesPublicBucketBeforeUpload()
    {
        var handler = new StorageHandler(true);
        var storage = Storage(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.UploadAsync("verification/a/b.jpg", [1]));
        Assert.Equal(new[] { HttpMethod.Get }, handler.Methods);
    }
    [Fact]
    public async Task SignedUrlsAreShortLivedAndNeverPublic()
    {
        var handler = new StorageHandler(false);
        var signed = await Storage(handler).SignAsync("verification/a/b.jpg");
        Assert.StartsWith("https://project.supabase.co/storage/v1/object/sign/verification-private/", signed);
        Assert.Contains("\"expiresIn\":120", handler.Body);
        Assert.DoesNotContain("object/public", signed);
    }
    [Theory]
    [InlineData("https://evil.example/storage/v1/object/public/images/a.jpg")]
    [InlineData("http://project.supabase.co/storage/v1/object/public/images/a.jpg")]
    [InlineData("https://project.supabase.co/storage/v1/object/public/other/a.jpg")]
    public void MigrationDoesNotDownloadUntrustedOriginsOrBuckets(string url)
        => Assert.False(Storage(new StorageHandler(false)).TryParseLegacyUrl(url, out _));

    [Fact]
    public async Task OwnerPurposeReadinessAndDeletionAreEnforcedBeforeSigning()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE VerificationMedia (Id TEXT PRIMARY KEY, OwnerId TEXT NOT NULL, ContextId TEXT NULL, Purpose TEXT NOT NULL, ObjectKey TEXT NOT NULL,
                ContentType TEXT NOT NULL, Sha256 TEXT NOT NULL, Size INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                ReadyAt TEXT NULL, AttachedAt TEXT NULL, DeletedAt TEXT NULL, StorageDeletedAt TEXT NULL, StorageLocationId TEXT NULL, LegacyLocationVerified INTEGER NOT NULL DEFAULT 0, LegacyObjectKey TEXT NULL, LegacySha256 TEXT NULL, LegacyDeletedAt TEXT NULL);
            """);
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var item = new VerificationMedia { Id = id, OwnerId = owner, Purpose = "identity-front", ObjectKey = "verification/test.jpg", CreatedAt = DateTime.UtcNow, ReadyAt = DateTime.UtcNow, AttachedAt = DateTime.UtcNow };
        db.VerificationMedia.Add(item); await db.SaveChangesAsync();
        var fake = new CaptureStorage();
        var service = new VerificationMediaService(db, fake);
        Assert.Null(await service.FindOwnedAsync(id, Guid.NewGuid(), "identity-front"));
        Assert.Null(await service.FindOwnedAsync(id, owner, "portrait"));
        Assert.Equal("", await service.ResolveAsync("https://public.example/cccd.jpg", owner, "identity-front"));
        var controller = new VerificationMediaController(service, db, fake, NullLogger<VerificationMediaController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(Guid.NewGuid(), UserRole.Customer) } } };
        Assert.IsType<NotFoundResult>(await controller.Access(id));
        Assert.Equal(0, fake.SignCalls);
        controller.HttpContext.User = Principal(owner, UserRole.MUA);
        Assert.IsType<OkObjectResult>(await controller.Access(id));
        controller.HttpContext.User = Principal(Guid.NewGuid(), UserRole.Admin);
        Assert.IsType<OkObjectResult>(await controller.Access(id));
        item.ReadyAt = null; await db.SaveChangesAsync();
        Assert.Null(await service.FindOwnedAsync(id, owner, "identity-front"));
        item.ReadyAt = DateTime.UtcNow; item.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.Access(id));
    }
    private static ClaimsPrincipal Principal(Guid id, UserRole role) => new(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role.ToString()) }, "test"));
    private static SupabaseVerificationStorage Storage(StorageHandler handler) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Supabase:Url"] = "https://project.supabase.co", ["Supabase:ServiceRoleKey"] = "test-server-key", ["Supabase:StorageBucket"] = "images"
        }).Build());
    private sealed class StorageHandler(bool isPublic) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = new();
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method);
            Assert.Equal("test-server-key", request.Headers.Authorization?.Parameter);
            if (request.Content != null) Body = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get
                ? $"{{\"public\":{isPublic.ToString().ToLowerInvariant()}}}"
                : "{\"signedURL\":\"/object/sign/verification-private/verification/a/b.jpg?token=test\"}") };
        }
    }
    public sealed class CaptureStorage : IVerificationStorage
    {
        public Task EnsurePrivateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public int SignCalls { get; private set; }
        public Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> SignAsync(string key, CancellationToken ct = default) { SignCalls++; return Task.FromResult("https://example.test/signed"); }
        public Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default) => Task.CompletedTask;
        public bool TryParseLegacyUrl(string url, out string key) { key = ""; return false; }
    }
}
