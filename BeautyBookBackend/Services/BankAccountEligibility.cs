using System.Linq.Expressions;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services;

public static class BankAccountEligibility
{
    public const string Pending = "PENDING_ADMIN";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public static Expression<Func<BankAccount, bool>> UsableAt(DateTime utcNow) =>
        account => account.IsActive
            && account.VerificationStatus == Approved;

    public static bool IsUsable(BankAccount account, DateTime utcNow) =>
        account.IsActive && account.VerificationStatus == Approved;

    public static bool IsUsableForOwner(BankAccount account, Guid userId, DateTime utcNow) =>
        account.UserId == userId && IsUsable(account, utcNow);

    public static bool CanReview(bool isActive, string verificationStatus) =>
        isActive && string.Equals(verificationStatus, Pending, StringComparison.Ordinal);

    public static bool IsCoolingDown(string verificationStatus, DateTime? activatedAt, DateTime utcNow) =>
        false;

    public static string? GetUnavailableReason(bool isActive, string verificationStatus, DateTime? activatedAt, DateTime utcNow)
    {
        if (string.Equals(verificationStatus, Rejected, StringComparison.Ordinal)) return "BANK_ACCOUNT_REJECTED";
        if (!isActive) return "BANK_ACCOUNT_NOT_FOUND";
        if (!string.Equals(verificationStatus, Approved, StringComparison.Ordinal)) return "BANK_ACCOUNT_PENDING_APPROVAL";
        return null;
    }

    public static bool HasSensitiveChanges(
        string currentCode,
        string currentAccountNumber,
        string currentAccountHolderName,
        string currentMethod,
        string? currentQrCodeUrl,
        string newCode,
        string newAccountNumber,
        string newAccountHolderName,
        string newMethod,
        string? newQrCodeUrl) =>
        !string.Equals(NormalizeCode(currentCode), NormalizeCode(newCode), StringComparison.Ordinal)
        || !string.Equals(NormalizeAccount(currentAccountNumber), NormalizeAccount(newAccountNumber), StringComparison.Ordinal)
        || !string.Equals(NormalizeHolder(currentAccountHolderName), NormalizeHolder(newAccountHolderName), StringComparison.Ordinal)
        || !string.Equals(NormalizeMethod(currentMethod), NormalizeMethod(newMethod), StringComparison.Ordinal);

    public static bool SnapshotMatches(Refund refund, BankAccount account) =>
        refund.DestinationCapturedAt.HasValue && account.UpdatedAt <= refund.DestinationCapturedAt.Value
        && refund.DestinationFinancialQrMediaId == account.FinancialQrMediaId
        && string.Equals(NormalizeCode(refund.DestinationBankCode), NormalizeCode(account.BankCode), StringComparison.Ordinal)
        && string.Equals(NormalizeCode(refund.DestinationBankBin), NormalizeCode(account.BankBin), StringComparison.Ordinal)
        && string.Equals(NormalizeRecipient(refund.DestinationBankCode,refund.DestinationAccountNumber), NormalizeRecipient(account.BankCode,account.AccountNumber), StringComparison.Ordinal)
        && string.Equals(NormalizeHolder(refund.DestinationAccountName), NormalizeHolder(account.AccountHolderName), StringComparison.Ordinal);

    private static string NormalizeRecipient(string? method,string? number) { if(method!="MOMO")return NormalizeAccount(number); try{return MomoPhone.Normalize(number);}catch(BookingRuleException){return NormalizeAccount(number);} }
    public static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    public static string NormalizeAccount(string? value) => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    public static string NormalizeHolder(string? value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static string NormalizeMethod(string? value) => string.Equals(value?.Trim(), "MOMO", StringComparison.OrdinalIgnoreCase) ? "MOMO" : "BANK";
    public static string NormalizeQr(string? value) => (value ?? string.Empty).Trim();
}
