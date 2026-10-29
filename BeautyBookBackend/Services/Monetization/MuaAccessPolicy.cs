using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services.Monetization;

public static class MuaAccessPolicy
{
    public static IQueryable<MakeupArtistProfile> Public(ApplicationDbContext db, IQueryable<MakeupArtistProfile> query) =>
        !db.PaidMuaEnforcement ? query : query.Where(m => !db.MuaMonetizationAccounts.Any(a => a.MuaId == m.MUAId
            && a.Cohort == MuaActivationCohort.PaidActivation && (a.ActivatedAt == null || a.ActivationRevokedAt != null || a.ActivationPurchaseReference == null)));
    public static async Task<bool> Allowed(ApplicationDbContext db, Guid owner, CancellationToken ct = default) =>
        !db.PaidMuaEnforcement || !await db.MuaMonetizationAccounts.AnyAsync(a => a.MuaId == owner && a.Cohort == MuaActivationCohort.PaidActivation
            && (a.ActivatedAt == null || a.ActivationRevokedAt != null || a.ActivationPurchaseReference == null), ct);
    internal static async Task Lock(ApplicationDbContext db, Guid owner, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            // Same order as existing writer guards; avoid holding an account lock
            // while waiting for an account-deletion/storage maintenance barrier.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(724266524669002)", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner.ToString()}, 772028))", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"MakeupArtistProfiles\" WHERE \"MUAId\"={owner} FOR UPDATE", ct);
        }
    }
}
