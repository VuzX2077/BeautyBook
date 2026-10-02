using System.Security.Cryptography;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class AccountDeletionStorage(ApplicationDbContext db, IVerificationStorage storage, IFinancialStorage? financialStorage = null)
{
    public async Task ProcessAsync(Guid owner, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var locked = false;
        try {
            locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(724266524669002) AS \"Value\"").SingleAsync(ct);
            if (!locked) return;
            var request = await db.AccountDeletionRequests.SingleAsync(x => x.UserId == owner, ct);
            if (request.DatabaseCompletedAt == null || request.Status == "Completed") return;
            if (!await db.Users.AnyAsync(x => x.UserId == owner && !x.IsActive && x.DeletedAt != null, ct))
                throw new InvalidOperationException("Account deletion precondition failed.");
            await LegacyMediaWriteGuard.VerifyCoverageAsync(db, ct);
            await AccountDeletionService.VerifyWriterCoverageAsync(db, ct);
            await FinancialMediaWriteGuard.VerifyCoverageAsync(db, ct);
            request.Attempts++; request.Status = "PendingStorage";
            await db.SaveChangesAsync(ct);
            var review = request.UnresolvedReferences.Count > 0;
            foreach (var item in await db.VerificationMedia.Where(x => x.OwnerId == owner).ToListAsync(ct)) {
                var isFinancial = item.Purpose == FinancialMediaService.Purpose;
                var target = isFinancial ? financialStorage ?? throw new InvalidOperationException("Financial storage is unavailable.") : storage;
                // A timed-out upload can have reached the provider despite a missing response.
                // Keep the durable manifest and defer confirmation while that upload settles.
                if (item.ReadyAt == null && item.CreatedAt > DateTime.UtcNow.AddMinutes(-10))
                    throw new InvalidOperationException("Incomplete upload requires a later deletion confirmation.");
                if (isFinancial && (item.ObjectKey != FinancialMediaService.ObjectKey(owner, item.Id) || item.StorageLocationId == null)) { review = true; continue; }
                if (item.ObjectKey != (isFinancial ? FinancialMediaService.ObjectKey(owner, item.Id) : $"verification/{owner:N}/{item.Id:N}.jpg") || item.DeletedAt == null)
                    throw new InvalidOperationException("Storage ownership invariant failed.");
                if (item.StorageLocationId == null) {
                    // Legacy metadata has no bucket binding. Never treat a 404 from a
                    // different project/bucket as proof that the original was erased.
                    try {
                        var bytes = await target.DownloadAsync(item.ObjectKey, ct: ct);
                        if (Convert.ToHexString(SHA256.HashData(bytes)) != item.Sha256) { review = true; continue; }
                        item.StorageLocationId = target.LocationId; await db.SaveChangesAsync(ct);
                    } catch (StorageObjectMissingException) { review = true; continue; }
                }
                if (item.StorageLocationId != target.LocationId)
                    throw new InvalidOperationException("Storage location differs from the recorded upload.");
                if (await HasReferenceAsync(VerificationMediaService.Reference(item.Id), ct)) { review = true; continue; }
                if (isFinancial && await FinancialMediaService.HasReferenceAsync(db, item.Id, ct)) { review = true; continue; }
                if (isFinancial && item.LegacyObjectKey != null && item.LegacyDeletedAt == null) { review = true; continue; }
                // Legacy source has a migration checksum, but must also be unreferenced now.
                if (item.LegacyObjectKey != null && item.LegacyDeletedAt == null) {
                    var canDeleteLegacy = item.LegacyLocationVerified && item.LegacySha256 != null && !await HasReferenceAsync(item.LegacyObjectKey, ct);
                    if (canDeleteLegacy) {
                        try {
                            var bytes = await storage.DownloadAsync(item.LegacyObjectKey, legacy: true, ct: ct);
                            canDeleteLegacy = Convert.ToHexString(SHA256.HashData(bytes)) == item.LegacySha256;
                        } catch (StorageObjectMissingException) { }
                    }
                    if (canDeleteLegacy) {
                        await storage.EnsurePrivateAsync(ct);
                        await DeleteAndConfirmAsync(item.LegacyObjectKey, true, ct);
                        item.LegacyDeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
                    } else review = true;
                }
                if (item.StorageDeletedAt == null) {
                    await target.EnsurePrivateAsync(ct);
                    await DeleteAndConfirmAsync(item.ObjectKey, false, ct, target);
                    item.StorageDeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
                }
                if (item.StorageDeletedAt != null && (item.LegacyObjectKey == null || item.LegacyDeletedAt != null)) {
                    item.Sha256 = ""; item.LegacySha256 = null; item.Size = 0; item.ContextId = null;
                    await db.SaveChangesAsync(ct);
                }
            }
            foreach (var item in await db.OwnedPublicMedia.Where(x => x.OwnerId == owner && x.StorageDeletedAt == null).ToListAsync(ct)) {
                if (item.ReadyAt == null && item.CreatedAt > DateTime.UtcNow.AddMinutes(-10))
                    throw new InvalidOperationException("Incomplete upload requires a later deletion confirmation.");
                if (!item.ObjectKey.StartsWith($"uploads/{owner:N}/{item.Id:N}.", StringComparison.Ordinal) || item.DeletedAt == null
                    || !storage.TryParseLegacyUrl(item.Url, out var parsed) || parsed != item.ObjectKey)
                    throw new InvalidOperationException("Storage ownership invariant failed.");
                if (await HasReferenceAsync(item.ObjectKey, ct)) { review = true; continue; }
                await DeleteAndConfirmAsync(item.ObjectKey, true, ct);
                item.StorageDeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
            }
            request.Status = review ? "NeedsReview" : "Completed";
            request.ErrorCode = review ? "LEGACY_OR_SHARED_MEDIA" : null;
            request.StorageCompletedAt = review ? null : DateTime.UtcNow;
            request.NextAttemptAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync(ct);
        } finally {
            try { if (locked) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(724266524669002)", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private async Task DeleteAndConfirmAsync(string key, bool legacy, CancellationToken ct, IVerificationStorage? target = null)
    {
        target ??= storage;
        await target.DeleteAsync(key, legacy, ct);
        try { await target.DownloadAsync(key, legacy, ct); }
        catch (StorageObjectMissingException) { return; }
        throw new InvalidOperationException("Storage deletion is not confirmed.");
    }

    private async Task<bool> HasReferenceAsync(string key, CancellationToken ct)
    {
        using var discover = db.Database.GetDbConnection().CreateCommand();
        discover.CommandText = "SELECT table_name,column_name FROM information_schema.columns WHERE table_schema='public' AND data_type IN ('text','character varying','json','jsonb','ARRAY') AND table_name NOT IN ('VerificationMedia','OwnedPublicMedia','AccountDeletionRequests','PrivateMediaJobs','__EFMigrationsHistory')";
        var columns = new List<(string Table, string Column)>();
        await using (var reader = await discover.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) columns.Add((reader.GetString(0), reader.GetString(1)));
        foreach (var (table, column) in columns) {
            using var command = db.Database.GetDbConnection().CreateCommand();
            static string Q(string x) => "\"" + x.Replace("\"", "\"\"") + "\"";
            command.CommandText = $"SELECT EXISTS(SELECT 1 FROM public.{Q(table)} WHERE strpos(lower(CAST({Q(column)} AS text)),lower(@key))>0 OR strpos(lower(CAST({Q(column)} AS text)),lower(@encoded))>0 OR strpos(lower(CAST({Q(column)} AS text)),lower(@fullEncoded))>0)";
            foreach (var (name, value) in new[] { ("key", key), ("encoded", string.Join('/', key.Split('/').Select(Uri.EscapeDataString))), ("fullEncoded", Uri.EscapeDataString(key)) }) {
                var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p);
            }
            if (await command.ExecuteScalarAsync(ct) is true) return true;
        }
        return false;
    }
}
