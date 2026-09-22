using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class Payout
    {
        public Guid Id { get; set; }
        public Guid MuaId { get; set; }
        public Guid RequestedBy { get; set; }
        public Guid? LastHandledBy { get; set; }
        public decimal Amount { get; set; }
        public PayoutStatus Status { get; set; }
        public PayoutProvider Provider { get; set; } = PayoutProvider.Manual;
        public string BankCodeSnapshot { get; set; } = string.Empty;
        public string? BankNameSnapshot { get; set; }
        public string AccountNumberSnapshot { get; set; } = string.Empty;
        public string AccountHolderNameSnapshot { get; set; } = string.Empty;
        public string? QrCodeUrlSnapshot { get; set; }
        public string? ProviderReference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public DateTime? ReconciledAt { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public DateTime UpdatedAt { get; set; }
        public MakeupArtistProfile? Mua { get; set; }
        public User? RequestedByUser { get; set; }
        public User? LastHandledByUser { get; set; }
        public ICollection<PayoutItem> Items { get; set; } = new List<PayoutItem>();
    }
}
