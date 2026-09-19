using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class BookingPayment
    {
        public Guid PaymentId { get; set; }
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public PaymentProvider Provider { get; set; } = PaymentProvider.PayOS;
        public long ProviderOrderCode { get; set; }
        public string? ProviderPaymentLinkId { get; set; }
        public string? ProviderReference { get; set; }
        public decimal Amount { get; set; }
        public BookingPaymentStatus Status { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public string? RawWebhookPayload { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? RefundRequestedAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Booking? Booking { get; set; }
        public User? Customer { get; set; }
    }
}
