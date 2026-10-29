using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services;

public sealed class MuaMonetizationOptions
{
    // Enabled still controls Phase 1 shadow reads only. These reserved rollout flags
    // do not enable a checkout, writer, email trigger or enforcement in this release.
    public bool Enabled { get; set; }
    public bool WebCheckoutEnabled { get; set; }
    public bool PaidEnforcementEnabled { get; set; }
    public bool SponsoredDeliveryEnabled { get; set; }
    public bool ApprovalEmailEnabled { get; set; }
    public bool EnrollPaidMuasEnabled { get; set; }
    public bool WorkersEnabled { get; set; }
    public bool PaymentLinksEnabled { get; set; }
    public bool EmailDeliveryEnabled { get; set; }
    public bool LivePaymentsEnabled { get; set; }
    public bool LiveEmailEnabled { get; set; }
    public bool WebPortalEnabled { get; set; }
}

public static class MuaMonetizationPolicy
{
    public const string PolicyVersion = "mua-v1";
    public const int VisibilityDays = 30;

    public static bool HasPermanentActivation(MuaMonetizationAccount? account) => true;

    // Caller of a future grant writer must hold a per-account DB lock. This method
    // only calculates an interval; it does not persist or grant any entitlement.
    public static (DateTime StartsAt, DateTime EndsAt) NextVisibilityInterval(DateTime now, IEnumerable<MuaVisibilityGrant> grants)
    {
        RequireUtc(now);
        var end = grants.Where(x => x.RevokedAt == null).Select(x => x.EndsAt).DefaultIfEmpty(now).Max();
        RequireUtc(end);
        var start = end > now ? end : now;
        return (start, start.AddDays(VisibilityDays));
    }

    public static MuaMonetizationDto Evaluate(MuaMonetizationAccount? account, IEnumerable<MuaVisibilityGrant> grants, DateTime now)
    {
        RequireUtc(now);
        var intervals = grants.OrderBy(x => x.StartsAt).ThenBy(x => x.Id).ToList();
        var valid = intervals.Where(x => x.RevokedAt == null).ToList();
        var current = valid.FirstOrDefault(x => x.StartsAt <= now && now < x.EndsAt);
        var next = valid.FirstOrDefault(x => x.StartsAt > now);
        var status = current?.Kind switch
        {
            VisibilityGrantKind.FirstActivationGift => "GiftActive",
            VisibilityGrantKind.Paid => "PaidActive",
            VisibilityGrantKind.Compensation => "CompensationActive",
            _ => next != null ? "Scheduled" : valid.Count > 0 ? "Expired" : intervals.Count > 0 ? "Revoked" : "None"
        };
        // Include contiguous scheduled grants, but do not bridge a gap or revoked grant.
        DateTime? visibleUntil = current?.EndsAt;
        if (visibleUntil.HasValue)
            foreach (var interval in valid.Where(x => x.StartsAt >= current!.StartsAt))
                if (interval.StartsAt <= visibleUntil.Value && interval.EndsAt > visibleUntil.Value) visibleUntil = interval.EndsAt;
        var hasActivation = HasPermanentActivation(account);
        return new()
        {
            Enabled = true,
            // Neither shadow mode nor a manually inserted record can turn on charging.
            EnforcementEnabled = false,
            ActivationCheckoutAvailable = false,
            VisibilityCheckoutAvailable = false,
            Cohort = account?.Cohort.ToString() ?? nameof(MuaActivationCohort.LegacyWaiver),
            ActivationStatus = "NotRequired",
            HasPermanentActivation = hasActivation,
            WouldRequireActivation = !hasActivation,
            ActivatedAt = account?.ActivatedAt,
            VisibilityStatus = status,
            VisibilityEndsAt = visibleUntil,
            EvaluatedAt = now,
            VisibilityIntervals = intervals.Select(x => new MuaVisibilityIntervalDto(x.Kind.ToString(), x.StartsAt, x.EndsAt, x.RevokedAt.HasValue)).ToList()
        };
    }

    private static void RequireUtc(DateTime time)
    {
        if (time.Kind != DateTimeKind.Utc) throw new ArgumentException("Monetization timestamps must use UTC.");
    }
}
