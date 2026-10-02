using System.Security.Cryptography;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class VerificationMediaService(ApplicationDbContext db, IVerificationStorage storage)
{
    public static readonly HashSet<string> Purposes = ["identity-front", "identity-back", "portrait", "certificate"];
    public static string Reference(Guid id) => $"media:{id:D}";
    public static bool TryId(string? reference, out Guid id)
    {
        id = default;
        return reference != null && reference.StartsWith("media:", StringComparison.Ordinal) && Guid.TryParse(reference[6..], out id) && id != Guid.Empty;
    }
    public async Task<VerificationMedia> UploadAsync(Guid owner, string purpose, byte[] input, CancellationToken ct = default)
    {
        if (!Purposes.Contains(purpose)) throw new ArgumentException("Loại ảnh xác minh không hợp lệ.");
        return await UploadCoreAsync(owner, purpose, input, null, ct);
    }
    public Task<VerificationMedia> UploadChatAsync(Guid owner, Guid roomId, byte[] input, CancellationToken ct = default) =>
        UploadCoreAsync(owner, "chat", input, roomId, ct);
    private async Task<VerificationMedia> UploadCoreAsync(Guid owner, string purpose, byte[] input, Guid? contextId, CancellationToken ct)
    {
        var bytes = VerificationImage.Normalize(input);
        var media = new VerificationMedia {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = purpose, ContextId = contextId, CreatedAt = DateTime.UtcNow,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length,
        };
        media.ObjectKey = $"verification/{owner:N}/{media.Id:N}.jpg";
        // Record first so a provider timeout/crash never leaves an untracked private object.
        db.VerificationMedia.Add(media);
        await db.SaveChangesAsync(ct);
        try
        {
            await storage.UploadAsync(media.ObjectKey, bytes, ct);
            media.ReadyAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            media.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return media;
    }
    public async Task<string?> ResolveChatAsync(string? reference, Guid roomId)
    {
        if (!TryId(reference, out var id)) return null;
        var item = await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ContextId == roomId && x.Purpose == "chat" && x.ReadyAt != null && x.DeletedAt == null);
        return item == null ? null : await storage.SignAsync(item.ObjectKey);
    }
    public Task<VerificationMedia?> FindOwnedAsync(Guid id, Guid owner, string purpose, CancellationToken ct = default) =>
        db.VerificationMedia.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner && x.Purpose == purpose && x.ReadyAt != null && x.DeletedAt == null, ct);

    public async Task<string> ResolveAsync(string? reference, Guid owner, string purpose, CancellationToken ct = default)
    {
        // Legacy public links must never be returned while migration is pending.
        if (!TryId(reference, out var id)) return "";
        var media = await FindOwnedAsync(id, owner, purpose, ct);
        return media == null ? "" : await storage.SignAsync(media.ObjectKey, ct);
    }
    public async Task<bool> HasIdentityAsync(MakeupArtistProfile profile)
    {
        foreach (var (reference, purpose) in new[] { (profile.IdentityFrontUrl, "identity-front"), (profile.IdentityBackUrl, "identity-back"), (profile.PortraitUrl, "portrait") })
            if (!TryId(reference, out var id) || await FindOwnedAsync(id, profile.MUAId, purpose) == null) return false;
        return true;
    }
}
