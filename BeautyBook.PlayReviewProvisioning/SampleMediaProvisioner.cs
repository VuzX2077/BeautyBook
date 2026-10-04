using System.Security.Cryptography;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BeautyBook.PlayReviewProvisioning;

public sealed class SampleMediaProvisioner(ApplicationDbContext db, IVerificationStorage privateStorage, IImageStorage publicStorage)
{
    public static readonly string[] AssetNames = ["identity-front", "identity-back", "portrait", "review-avatar", "counterpart-avatar", "portfolio-1", "portfolio-2", "portfolio-3"];

    public static Dictionary<string, byte[]> LoadAssets(ProvisioningSettings settings)
    {
        if (!settings.SampleAssetsAcknowledged) throw ProvisioningSettings.Error("SAMPLE_ASSETS_ATTESTATION_REQUIRED");
        var root = Path.GetFullPath(settings.AssetsDirectory);
        if (!Directory.Exists(root) || new DirectoryInfo(root).LinkTarget != null) throw ProvisioningSettings.Error("ASSETS_INVALID");
        var result = new Dictionary<string, byte[]>();
        foreach (var purpose in AssetNames)
        {
            if (!settings.AssetFiles.TryGetValue(purpose, out var name) || Path.GetFileName(name) != name) throw ProvisioningSettings.Error("ASSET_PURPOSE_INVALID");
            var path = Path.Combine(root, name);
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget != null || info.Length is <= 0 or > VerificationImage.MaxBytes
                || !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(info.Extension.ToLowerInvariant())) throw ProvisioningSettings.Error("ASSET_INVALID");
            var input = File.ReadAllBytes(path);
            if (input.Length > VerificationImage.MaxBytes) throw ProvisioningSettings.Error("ASSET_INVALID");
            if (settings.AssetChecksums.TryGetValue(purpose, out var expected)
                && !Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw ProvisioningSettings.Error("ASSET_CHECKSUM_MISMATCH");
            byte[] normalized;
            try { normalized = VerificationImage.Normalize(input); }
            catch { throw ProvisioningSettings.Error("ASSET_IMAGE_INVALID"); }
            using var bitmap = SKBitmap.Decode(normalized);
            if (bitmap.Width < 320 || bitmap.Height < 240) throw ProvisioningSettings.Error("ASSET_DIMENSIONS_INVALID");
            result[purpose] = purpose is "identity-front" or "identity-back" ? Watermark(normalized) : normalized;
        }
        return result;
    }

    private static byte[] Watermark(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        using var canvas = new SKCanvas(bitmap);
        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 240) };
        canvas.DrawRect(0, bitmap.Height * .33f, bitmap.Width, bitmap.Height * .34f, background);
        using var paint = new SKPaint { Color = SKColors.DarkRed, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, Math.Min(bitmap.Width / 20f, bitmap.Height / 15f));
        string[] lines = ["SAMPLE", "NOT A REAL ID", "FOR APP REVIEW"];
        for (var i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], bitmap.Width / 2f, bitmap.Height * .43f + i * font.Size * 1.2f, SKTextAlign.Center, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return VerificationImage.Normalize(encoded.ToArray());
    }

    public async Task EnsureAsync(ProvisioningSettings settings, Dictionary<string, byte[]> assets, CancellationToken ct)
    {
        await using var mediaLock = new MediaOperationLock(db);
        await mediaLock.AcquireAsync(ct);
        await privateStorage.EnsurePrivateAsync(ct);
        var profile = await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == settings.ReviewUserId, ct);
        foreach (var purpose in new[] { "identity-front", "identity-back", "portrait" })
        {
            var reference = purpose switch { "identity-front" => profile.IdentityFrontUrl, "identity-back" => profile.IdentityBackUrl, _ => profile.PortraitUrl };
            if (VerificationMediaService.TryId(reference, out var existingId))
            {
                var existing = await db.VerificationMedia.SingleAsync(x => x.Id == existingId, ct);
                await VerifyStoredAsync(existing, ct);
                continue;
            }
            var bytes = assets[purpose]; var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var attempt = 0;
            VerificationMedia? item;
            do { item = await db.VerificationMedia.FindAsync([settings.Id($"media:{purpose}:{hash}:{attempt}")], ct); if (item?.DeletedAt != null) attempt++; }
            while (item?.DeletedAt != null);
            if (item == null)
            {
                item = new VerificationMedia { Id = settings.Id($"media:{purpose}:{hash}:{attempt}"), OwnerId = settings.ReviewUserId, Purpose = purpose,
                    ContentType = "image/jpeg", Sha256 = hash, Size = bytes.Length, CreatedAt = DateTime.UtcNow, StorageLocationId = privateStorage.LocationId };
                item.ObjectKey = $"verification/{item.OwnerId:N}/{item.Id:N}.jpg";
                db.VerificationMedia.Add(item); await db.SaveChangesAsync(ct);
            }
            if (item.OwnerId != settings.ReviewUserId || item.Purpose != purpose || item.Sha256 != hash || item.StorageLocationId != privateStorage.LocationId) throw ProvisioningSettings.Error("MEDIA_COLLISION");
            try
            {
                if (item.ReadyAt == null)
                {
                    // A previous process may have uploaded then crashed before persisting Ready.
                    try { var stored = await privateStorage.DownloadAsync(item.ObjectKey, ct: ct); if (Convert.ToHexString(SHA256.HashData(stored)) != hash) throw ProvisioningSettings.Error("MEDIA_CONTENT_MISMATCH"); }
                    catch (StorageObjectMissingException) { await privateStorage.UploadAsync(item.ObjectKey, bytes, ct); }
                    item.ReadyAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
                }
                await VerifyStoredAsync(item, ct);
                await using var attach = await db.Database.BeginTransactionAsync(ct);
                if (purpose == "identity-front") profile.IdentityFrontUrl = VerificationMediaService.Reference(item.Id);
                else if (purpose == "identity-back") profile.IdentityBackUrl = VerificationMediaService.Reference(item.Id);
                else profile.PortraitUrl = VerificationMediaService.Reference(item.Id);
                item.AttachedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct); await attach.CommitAsync(ct);
            }
            catch
            {
                db.ChangeTracker.Clear();
                // Uploaded Ready resources without a committed profile reference remain cleanup-safe.
                throw ProvisioningSettings.Error("SAMPLE_MEDIA_OPERATION_FAILED");
            }
        }
        foreach (var owner in new[] { settings.ReviewUserId, settings.CounterpartUserId })
        {
            var user = await db.Users.SingleAsync(x => x.UserId == owner, ct);
            if (string.IsNullOrWhiteSpace(user.AvatarUrl))
            {
                using var stream = new MemoryStream(assets[owner == settings.ReviewUserId ? "review-avatar" : "counterpart-avatar"]);
                user.AvatarUrl = await publicStorage.UploadOwnedPublicImageAsync(owner, stream, "image/jpeg", ".jpg", ct);
                await db.SaveChangesAsync(ct);
            }
            var portfolio = await db.Portfolios.SingleAsync(x => x.PortfolioId == settings.Id($"portfolio:{owner:D}"), ct);
            if (portfolio.ImageUrls.Count == 0)
            {
                var urls = new List<string>();
                for (var i = 1; i <= 3; i++) { using var stream = new MemoryStream(assets[$"portfolio-{i}"]); urls.Add(await publicStorage.UploadOwnedPublicImageAsync(owner, stream, "image/jpeg", ".jpg", ct)); }
                portfolio.ImageUrls = urls; await db.SaveChangesAsync(ct);
            }
        }
    }
    public async Task VerifyStoredAsync(VerificationMedia item, CancellationToken ct)
    {
        if (item.ReadyAt == null || item.DeletedAt != null || item.StorageLocationId != privateStorage.LocationId) throw ProvisioningSettings.Error("MEDIA_NOT_READY");
        var bytes = await privateStorage.DownloadAsync(item.ObjectKey, ct: ct);
        if (bytes.Length != item.Size || Convert.ToHexString(SHA256.HashData(bytes)) != item.Sha256) throw ProvisioningSettings.Error("MEDIA_CONTENT_MISMATCH");
    }
}
