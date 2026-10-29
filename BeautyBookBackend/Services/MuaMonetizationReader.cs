using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Services;

public sealed class MuaMonetizationReader(ApplicationDbContext db, IOptions<MuaMonetizationOptions> options, TimeProvider clock)
{
    public async Task<MuaMonetizationDto?> GetAsync(Guid muaId, CancellationToken cancellationToken = default)
    {
        // Critical for expand-first deployment: disabled requests never query new tables.
        if (!options.Value.Enabled) return null;
        var profile = await db.MakeupArtistProfiles.AsNoTracking().Where(x => x.MUAId == muaId
            && x.User != null && x.User.IsActive && x.User.DeletedAt == null && !x.User.IsDemoAccount).Select(x => new { x.VerificationStatus }).SingleOrDefaultAsync(cancellationToken);
        if (profile == null) return null;
        var account = await db.MuaMonetizationAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.MuaId == muaId, cancellationToken);
        var grants = await db.MuaVisibilityGrants.AsNoTracking().Where(x => x.MuaId == muaId).ToListAsync(cancellationToken);
        return MuaMonetizationPolicy.Evaluate(account, grants, clock.GetUtcNow().UtcDateTime) with { EnforcementEnabled = false, ReviewStatus = profile.VerificationStatus.ToString() };
    }
}
