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
        public string? MaskedDestinationAccountNumber { get; set; }
        public string? DestinationBankName { get; set; }
        public string? DestinationAccountName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
    }

    public class CustomerBankAccountDto
    {
        public Guid Id { get; set; }
        public string BankBin { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public string Method { get; set; } = "BANK";
        public string? QrCodeUrl { get; set; }
        public DateTime ActivatedAt { get; set; }
        public bool IsCoolingDown { get; set; }
    }

    public class UpsertCustomerBankAccountRequest
    {
        [Required, RegularExpression("^([0-9]{6}|MOMO)$")]
        public string BankBin { get; set; } = string.Empty;
        [MaxLength(100)] public string? BankName { get; set; }
        [Required, RegularExpression("^[A-Za-z0-9]{5,30}$")]
        public string AccountNumber { get; set; } = string.Empty;
        [Required, MaxLength(150)] public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        [Required] public string CurrentPassword { get; set; } = string.Empty;
        public string Method { get; set; } = "BANK";
        [Url, MaxLength(1000)] public string? QrCodeUrl { get; set; }
    }

    public class RefundDestinationRequest
    {
        [Required] public Guid BankAccountId { get; set; }
    }

    public class SensitiveActionRequest
    {
        [Required] public string CurrentPassword { get; set; } = string.Empty;
    }

    public class AdminRefundDto : RefundSummaryDto
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? DestinationBankBin { get; set; }
        public string? DestinationAccountNumber { get; set; }
        public string? DestinationQrCodeUrl { get; set; }
        public int AttemptCount { get; set; }
        public string? ProviderPayoutId { get; set; }
        public string? LastProviderState { get; set; }
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
