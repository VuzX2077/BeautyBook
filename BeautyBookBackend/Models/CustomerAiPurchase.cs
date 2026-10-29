namespace BeautyBookBackend.Models;

// Payment ledger and permanent access are tied to the authenticated BBook account.
public sealed class CustomerAiPurchase
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public long OrderCode { get; set; }
    public string Channel { get; set; } = "";
    public string? PaymentLinkId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? LinkCreationStartedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string State { get; set; } = "PaymentPending";
}
