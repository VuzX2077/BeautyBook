using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed class SetBankAccountDefaultRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
}

public class BankAccountDraftRequest
{
    [Required, MaxLength(20)] public string BankCode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string BankBin { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string BankName { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string AccountNumber { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountHolderName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Method { get; set; } = "BANK";
    [Url, MaxLength(1000)] public string? QrCodeUrl { get; set; }
}

public sealed class UpsertBankAccountRequest : BankAccountDraftRequest
{
    public string Otp { get; set; } = string.Empty;
}

public sealed class BankAccountOtpResponse
{
    public string MaskedEmail { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; } = 300;
    public int ResendAfterSeconds { get; set; } = 60;
}

public sealed class BankAccountDto
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string Method { get; set; } = "BANK";
    public string? QrCodeUrl { get; set; }
    public string VerificationStatus { get; set; } = "PENDING_ADMIN";
    public DateTime? ActivatedAt { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public bool IsUsable { get; set; }
    public bool CanReceiveMoney { get; set; }
    public bool IsCoolingDown { get; set; }
    public string? UnavailableReason { get; set; }
}
