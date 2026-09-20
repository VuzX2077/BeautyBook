using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class UpsertMuaBankAccountRequest
    {
        [Required, StringLength(20, MinimumLength=2)] public string BankCode { get; set; } = string.Empty;
        [StringLength(100)] public string? BankName { get; set; }
        [Required, StringLength(30, MinimumLength=5)] public string AccountNumber { get; set; } = string.Empty;
        [Required, StringLength(150, MinimumLength=2)] public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }
    public class MuaBankAccountDto
    {
        public Guid Id { get; set; }
        public string BankCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string VerificationStatus { get; set; } = "Entered";
    }
    public class CreatePayoutRequest
    {
        [Required] public Guid BankAccountId { get; set; }
        public IReadOnlyList<Guid>? ReceivableIds { get; set; }
        [Required, StringLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    }
    public class PayoutActionRequest { [StringLength(255)] public string? Reference { get; set; } }
    public class PayoutCompleteRequest { [Required, StringLength(255)] public string Reference { get; set; } = string.Empty; }
    public class PayoutFailRequest
    {
        [Required, StringLength(100)] public string FailureCode { get; set; } = string.Empty;
        [Required, StringLength(1000)] public string FailureMessage { get; set; } = string.Empty;
        public bool ConfirmedFundsNotSent { get; set; }
    }
    public class PayoutDto
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public PayoutStatus Status { get; set; }
        public PayoutProvider Provider { get; set; }
        public string BankCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public string? ProviderReference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public IReadOnlyList<Guid> ReceivableIds { get; set; } = Array.Empty<Guid>();
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public DateTime? ReconciledAt { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
    }
    public class AdminPayoutDto : PayoutDto
    {
        public string AccountNumber { get; set; } = string.Empty;
    }
}
