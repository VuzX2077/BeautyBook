using System.Security.Cryptography;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Only recorded failed/replaced/unattached financial uploads, never legacy public files.
public sealed class FinancialMediaCleanup(ApplicationDbContext db, IFinancialStorage storage)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var locked = false;
        try {
            locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(724266524669002) AS \"Value\"").SingleAsync(ct);
            if (!locked) return;
            await FinancialMediaWriteGuard.VerifyCoverageAsync(db, ct);
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var settling = DateTime.UtcNow.AddMinutes(-10);
            foreach (var item in await db.VerificationMedia.Where(x => x.Purpose == FinancialMediaService.Purpose && x.StorageDeletedAt == null && x.CreatedAt < settling && (x.DeletedAt != null || (x.AttachedAt == null && x.CreatedAt < cutoff))).OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct)) {
                if (await FinancialMediaService.HasReferenceAsync(db, item.Id, ct)) continue;
                if (item.ObjectKey != FinancialMediaService.ObjectKey(item.OwnerId, item.Id) || item.StorageLocationId != storage.LocationId || item.LegacyObjectKey != null)
                    throw new InvalidOperationException("Financial deletion ownership/location requires review.");
                item.DeletedAt ??= DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await storage.EnsurePrivateAsync(ct);
                try {
                    var bytes = await storage.DownloadAsync(item.ObjectKey, ct: ct);
                    if (Convert.ToHexString(SHA256.HashData(bytes)) != item.Sha256) throw new InvalidOperationException("Financial deletion checksum differs.");
                    await storage.DeleteAsync(item.ObjectKey, ct: ct);
                    try { await storage.DownloadAsync(item.ObjectKey, ct: ct); }
                    catch (StorageObjectMissingException) { item.StorageDeletedAt = DateTime.UtcNow; }
                    if (item.StorageDeletedAt == null) throw new InvalidOperationException("Financial deletion is not confirmed.");
                } catch (StorageObjectMissingException) { item.StorageDeletedAt = DateTime.UtcNow; }
                await db.SaveChangesAsync(ct);
            }
        } finally {
            try { if (locked) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(724266524669002)", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}

public sealed class FinancialMediaCleanupWorker(IServiceScopeFactory scopes, ILogger<FinancialMediaCleanupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<FinancialMediaCleanup>().ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning("Financial cleanup postponed ({FailureType}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
