namespace BeautyBookBackend.Models;

// Foundation only: no public checkout endpoint or production worker uses these records.
public enum MuaCommerceOrderState { PaymentPending, PaidAwaitingFulfillment, ManualReview, Fulfilled, Cancelled, Refunded }

public sealed class MuaCommerceOrder
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public string ProductId { get; set; } = "";
    public int AmountVnd { get; set; }
    public string Currency { get; set; } = "VND";
    public string IdempotencyKey { get; set; } = "";
    public string TermsVersion { get; set; } = "";
    public string Origin { get; set; } = "IndependentWebsite";
    public MuaCommerceOrderState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedPaidAt { get; set; }
    public bool PortfolioPromotionConsent { get; set; }
}

public sealed class MuaCommerceAttempt
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid OrderId { get; set; }
    public long OrderCode { get; set; }
    public string Channel { get; set; } = "";
    public string? PaymentLinkId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? CreationStartedAt { get; set; }
}

// Transport-level jobs have no user-owner FK. This allows retaining and triaging
// money evidence after account deletion without bypassing existing owner guards.
public sealed class MuaCommerceJob
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
    public string Kind { get; set; } = "";
    public string DedupKey { get; set; } = "";
    public string State { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime AvailableAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTime? FirstSendAt { get; set; }
    public string? DeliveryState { get; set; }
    public string? RecipientHash { get; set; }
}

public sealed class MuaCommerceIngress
{
    public Guid Id { get; set; }
    public string EventKey { get; set; } = "";
    public string EvidenceHash { get; set; } = "";
    public long OrderCode { get; set; }
    public long AmountVnd { get; set; }
    public string PaymentLinkId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Source { get; set; } = "";
    public string State { get; set; } = "Pending";
    public DateTime ReceivedAt { get; set; }
}

public sealed class MuaCommerceOffer
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid AdminId { get; set; }
    public string Kind { get; set; } = "Requested";
    public string EvidenceReference { get; set; } = "";
    public string EmailSnapshot { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class MuaCommerceInbox
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid OrderId { get; set; }
    public string EventKey { get; set; } = "";
    public string EvidenceHash { get; set; } = "";
    public string Source { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}

public sealed class MuaCommerceOutbox
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid OrderId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime AvailableAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
}

public sealed class MuaCommerceAudit
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid OrderId { get; set; }
    public string Action { get; set; } = "";
    public string EvidenceHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class MuaWebSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string CsrfToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class MuaFacebookTask
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public Guid GrantId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime CycleStartsAt { get; set; }
    public DateTime DueAt { get; set; }
    public string State { get; set; } = "Preparing";
    public string Draft { get; set; } = "";
    public string? PublishedUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Revisions { get; set; }
    public string? RevisionRequest { get; set; }
    public string? RemedyNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MuaSponsoredEvent
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public string Kind { get; set; } = "Impression";
    public DateTime CreatedAt { get; set; }
}
