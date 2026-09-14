using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class WalletTopUp
    {
        public Guid TopUpId { get; set; }
        public Guid UserId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Amount { get; set; }
        public PaymentProvider Provider { get; set; }
        public long ProviderOrderCode { get; set; }
        public string? ProviderPaymentLinkId { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public TopUpStatus Status { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
        public string? ProviderReference { get; set; }
        public string? RawWebhookPayload { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public User? User { get; set; }
        public Wallet? Wallet { get; set; }
    }
}
