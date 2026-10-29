namespace BeautyBookBackend.Models;

// Values are persisted as strings. A waiver is not a paid transaction.
public enum MuaActivationCohort { LegacyWaiver, RolloutWaiver, PaidActivation }
public enum VisibilityGrantKind { FirstActivationGift, Paid, Compensation }

public sealed class MuaMonetizationAccount
{
    public Guid MuaId { get; set; }
    public MuaActivationCohort Cohort { get; set; } = MuaActivationCohort.PaidActivation;
    public string PolicyVersion { get; set; } = "mua-v1";
    public DateTime EnrolledAt { get; set; }
    // Populated only by a future verified-payment processor. Never by a client DTO.
    public string? ActivationPurchaseReference { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ActivationRevokedAt { get; set; }
    public DateTime? FirstPublishedAt { get; set; }
}

public sealed class MuaVisibilityGrant
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public VisibilityGrantKind Kind { get; set; } = VisibilityGrantKind.Paid;
    // Globally unique provider/campaign reference; not a purchase token.
    public string SourceReference { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime CreatedAt { get; set; }
}
