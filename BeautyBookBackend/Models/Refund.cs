using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class Refund
    {
        public Guid RefundId { get; set; }
        public Guid BookingId { get; set; }
        public Guid BookingPaymentId { get; set; }
        public decimal Amount { get; set; }
        public RefundStatus Status { get; set; }
        public RefundReasonCode ReasonCode { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid? RequestedBy { get; set; }
        public Guid? LastHandledBy { get; set; }
        public string? ProviderReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Booking? Booking { get; set; }
        public BookingPayment? BookingPayment { get; set; }
        public User? RequestedByUser { get; set; }
        public User? LastHandledByUser { get; set; }
    }
}
