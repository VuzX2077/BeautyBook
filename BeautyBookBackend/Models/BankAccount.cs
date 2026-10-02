namespace BeautyBookBackend.Models;

public sealed class BankAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string Method { get; set; } = "BANK";
    public string CanonicalBankKey { get; set; } = string.Empty;
    public string NormalizedAccountNumber { get; set; } = string.Empty;
    public string? QrCodeUrl { get; set; }
    public Guid? FinancialQrMediaId { get; set; }
    public string VerificationStatus { get; set; } = "PENDING_ADMIN";
    public DateTime? ActivatedAt { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User? User { get; set; }
}
