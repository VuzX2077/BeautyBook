using System.Security.Cryptography;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Explicit operator commands only: never migrate/delete production storage on startup.
public sealed class VerificationMediaMaintenance(ApplicationDbContext db, VerificationMediaService media, IVerificationStorage storage, ILogger<VerificationMediaMaintenance> log)
{
    public async Task RunAsync(string action, CancellationToken ct = default)
    {
        if (action is not ("audit" or "migrate" or "cleanup-legacy" or "cleanup-orphans")) throw new ArgumentException("Unknown verification maintenance action.");
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(724266524669002)", ct);
            if (action is "audit" or "migrate") await MigrateAsync(action == "migrate", ct);
            else if (action == "cleanup-legacy") await CleanupLegacyAsync(ct);
            else await CleanupOrphansAsync(ct);
        }
        finally
        {
            try { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(724266524669002)", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    private async Task MigrateAsync(bool apply, CancellationToken ct)
    {
        var profiles = await db.MakeupArtistProfiles.OrderBy(x => x.MUAId).ToListAsync(ct);
        int converted = 0, unsupported = 0, shared = 0;
        foreach (var profile in profiles)
        {
            async Task<string?> ConvertAsync(string? reference, string purpose)
            {
                if (string.IsNullOrEmpty(reference)) return reference;
                if (VerificationMediaService.TryId(reference, out var existingId))
                {
                    if (await media.FindOwnedAsync(existingId, profile.MUAId, purpose, ct) == null) unsupported++;
                    return reference;
                }
                if (!storage.TryParseLegacyUrl(reference, out var key)) { unsupported++; return reference; }
                // Shared with any other surface? Do not silently remove a public avatar/portfolio.
                if (await HasExternalReferenceAsync(key, ct)) { shared++; return reference; }
                converted++;
                if (!apply) return reference;
                var source = await storage.DownloadAsync(key, legacy: true, ct: ct);
                var normalized = VerificationImage.Normalize(source);
                var digest = Convert.ToHexString(SHA256.HashData(normalized));
                var item = await db.VerificationMedia.FirstOrDefaultAsync(x => x.OwnerId == profile.MUAId && x.Purpose == purpose && x.LegacyObjectKey == key && x.ReadyAt != null && x.DeletedAt == null, ct);
                if (item == null)
                {
                    item = await media.UploadAsync(profile.MUAId, purpose, source, ct);
                    item.LegacyObjectKey = key;
                    item.LegacySha256 = Convert.ToHexString(SHA256.HashData(source));
                    item.LegacyLocationVerified = true;
                    await db.SaveChangesAsync(ct);
                }
                var stored = await storage.DownloadAsync(item.ObjectKey, ct: ct);
                if (digest != item.Sha256 || Convert.ToHexString(SHA256.HashData(stored)) != item.Sha256)
                    throw new InvalidOperationException("Migration checksum mismatch; original retained.");
                item.AttachedAt = DateTime.UtcNow;
                return VerificationMediaService.Reference(item.Id);
            }
            // Save one field at a time after its verified private copy is durable.
            profile.IdentityFrontUrl = await ConvertAsync(profile.IdentityFrontUrl, "identity-front");
            if (apply) await db.SaveChangesAsync(ct);
            profile.IdentityBackUrl = await ConvertAsync(profile.IdentityBackUrl, "identity-back");
            if (apply) await db.SaveChangesAsync(ct);
            profile.PortraitUrl = await ConvertAsync(profile.PortraitUrl, "portrait");
            if (apply) await db.SaveChangesAsync(ct);
            for (var i = 0; i < profile.CertificateUrls.Count; i++)
            {
                profile.CertificateUrls[i] = (await ConvertAsync(profile.CertificateUrls[i], "certificate"))!;
                if (apply) await db.SaveChangesAsync(ct);
            }
        }
        var messages = await db.Messages.Where(x => x.ImageUrl != null).OrderBy(x => x.MessageId).ToListAsync(ct);
        foreach (var message in messages)
        {
            if (VerificationMediaService.TryId(message.ImageUrl, out var existingId))
            {
                var existing = await media.FindOwnedAsync(existingId, message.SenderId, "chat", ct);
                if (existing?.ContextId != message.ChatRoomId) unsupported++;
                continue;
            }
            if (!storage.TryParseLegacyUrl(message.ImageUrl!, out var key)) { unsupported++; continue; }
            if (await HasExternalReferenceAsync(key, ct, skipDocuments: false, skipMessages: true)) { shared++; continue; }
            converted++;
            if (!apply) continue;
            var source = await storage.DownloadAsync(key, legacy: true, ct: ct);
            var digest = Convert.ToHexString(SHA256.HashData(VerificationImage.Normalize(source)));
            var item = await db.VerificationMedia.FirstOrDefaultAsync(x => x.OwnerId == message.SenderId && x.Purpose == "chat" && x.ContextId == message.ChatRoomId && x.LegacyObjectKey == key && x.ReadyAt != null && x.DeletedAt == null, ct);
            if (item == null)
            {
                item = await media.UploadChatAsync(message.SenderId, message.ChatRoomId, source, ct);
                item.LegacyObjectKey = key;
                item.LegacySha256 = Convert.ToHexString(SHA256.HashData(source));
                item.LegacyLocationVerified = true;
                await db.SaveChangesAsync(ct);
            }
            var stored = await storage.DownloadAsync(item.ObjectKey, ct: ct);
            if (digest != item.Sha256 || Convert.ToHexString(SHA256.HashData(stored)) != item.Sha256) throw new InvalidOperationException("Chat migration checksum mismatch; original retained.");
            item.AttachedAt = DateTime.UtcNow;
            message.ImageUrl = VerificationMediaService.Reference(item.Id);
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Private media {Action}: {Count} candidates, {Unsupported} unsupported, {Shared} shared objects. No public objects deleted.", apply ? "migration" : "audit", converted, unsupported, shared);
        if (unsupported > 0 || shared > 0) throw new InvalidOperationException("Unresolved legacy references remain; migration is not complete.");
    }
    private async Task CleanupLegacyAsync(CancellationToken ct)
    {
        // Fail before any DELETE if the provider cannot prove the bucket is private now.
        await storage.EnsurePrivateAsync(ct);
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db, ct);
        var rows = await db.VerificationMedia.Where(x => x.LegacyObjectKey != null && x.LegacyDeletedAt == null && x.DeletedAt == null && x.ReadyAt != null && x.AttachedAt != null).ToListAsync(ct);
        foreach (var item in rows)
        {
            var key = item.LegacyObjectKey!;
            if (await HasAnyProfileLegacyReferenceAsync(key, ct) || await HasExternalReferenceAsync(key, ct, skipDocuments: false))
                throw new InvalidOperationException("Original still referenced; cleanup refused.");
            var copy = await storage.DownloadAsync(item.ObjectKey, ct: ct);
            if (Convert.ToHexString(SHA256.HashData(copy)) != item.Sha256) throw new InvalidOperationException("Private copy checksum mismatch; cleanup refused.");
            try
            {
                var original = await storage.DownloadAsync(key, legacy: true, ct: ct);
                if (Convert.ToHexString(SHA256.HashData(original)) != item.LegacySha256)
                    throw new InvalidOperationException("Original changed after migration; cleanup refused.");
            }
            catch (StorageObjectMissingException) { /* Retry after a previous successful storage deletion. */ }
            // Provider DELETE is idempotent. A crash before DB marking safely repeats it.
            await storage.EnsurePrivateAsync(ct);
            await storage.DeleteAsync(key, legacy: true, ct: ct);
            item.LegacyDeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Removed {Count} verified legacy storage objects. Check CDN caches and anonymous access separately.", rows.Count);
    }
    private async Task CleanupOrphansAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var rows = await db.VerificationMedia.Where(x => x.CreatedAt < cutoff && (x.LegacyObjectKey == null || x.LegacyDeletedAt != null)
            && !db.AccountDeletionRequests.Any(r => r.UserId == x.OwnerId && r.DatabaseCompletedAt != null)).ToListAsync(ct);
        var count = 0;
        foreach (var item in rows)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"VerificationMedia\" WHERE \"Id\" = {item.Id} FOR UPDATE", ct);
            var reference = VerificationMediaService.Reference(item.Id);
            if (await db.MakeupArtistProfiles.AnyAsync(x => x.IdentityFrontUrl == reference || x.IdentityBackUrl == reference || x.PortraitUrl == reference || x.CertificateUrls.Contains(reference), ct)
                || await db.Messages.AnyAsync(x => x.ImageUrl == reference, ct)
) continue;
            item.DeletedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await storage.DeleteAsync(item.ObjectKey, ct: ct);
            // Keep legacy metadata as a permanent URL tombstone after storage deletion.
            if (item.LegacyObjectKey == null) db.VerificationMedia.Remove(item);
            await db.SaveChangesAsync(ct);
            count++;
        }
        log.LogInformation("Removed {Count} unreferenced private verification uploads older than 24 hours.", count);
    }
    private async Task<bool> HasAnyProfileLegacyReferenceAsync(string key, CancellationToken ct)
    {
        // Check by object key, including duplicate/shared uses of the same object.
        var profiles = await db.MakeupArtistProfiles.AsNoTracking().ToListAsync(ct);
        return profiles.SelectMany(x => new[] { x.IdentityFrontUrl, x.IdentityBackUrl, x.PortraitUrl }.Concat(x.CertificateUrls))
            .Any(x => x != null && storage.TryParseLegacyUrl(x, out var candidate) && candidate == key);
    }
    private async Task<bool> HasExternalReferenceAsync(string key, CancellationToken ct, bool skipDocuments = true, bool skipMessages = false)
    {
        // Discover all textual columns instead of overlooking chat, reviews, bank QR,
        // JSON fields, or new public features added later. Never print their values.
        var connection = db.Database.GetDbConnection();
        using var discover = connection.CreateCommand();
        discover.CommandText = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' AND (data_type IN ('text', 'character varying', 'json', 'jsonb', 'ARRAY')) AND table_name NOT IN ('VerificationMedia', '__EFMigrationsHistory')";
        var columns = new List<(string Table, string Column)>();
        await using (var reader = await discover.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) columns.Add((reader.GetString(0), reader.GetString(1)));
        foreach (var (table, column) in columns)
        {
            if (skipDocuments && table == "MakeupArtistProfiles" && column is "IdentityFrontUrl" or "IdentityBackUrl" or "PortraitUrl" or "CertificateUrls") continue;
            if (skipMessages && table == "Messages" && column == "ImageUrl") continue;

            using var command = connection.CreateCommand();
            static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
            command.CommandText = $"SELECT EXISTS(SELECT 1 FROM public.{Quote(table)} WHERE strpos(CAST({Quote(column)} AS text), @needle) > 0 OR strpos(CAST({Quote(column)} AS text), @encoded) > 0)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "needle"; parameter.Value = key; command.Parameters.Add(parameter);
            var encoded = command.CreateParameter(); encoded.ParameterName = "encoded"; encoded.Value = string.Join('/', key.Split('/').Select(Uri.EscapeDataString)); command.Parameters.Add(encoded);
            if (await command.ExecuteScalarAsync(ct) is true) return true;
        }
        return false;
    }
}
