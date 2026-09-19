using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class RefundSummaryDto
    {
        public Guid RefundId { get; set; }
        public decimal Amount { get; set; }
        public RefundStatus Status { get; set; }
        public RefundReasonCode ReasonCode { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? ProviderReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
    }

    public class RefundProcessingRequest
    {
        [MaxLength(255)]
        public string? Reference { get; set; }
    }

    public class RefundCompletionRequest
    {
        [Required, MaxLength(255)]
        public string Reference { get; set; } = string.Empty;
    }

    public class RefundFailureRequest
    {
        [Required, MaxLength(100)]
        public string FailureCode { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string FailureMessage { get; set; } = string.Empty;
    }
}
