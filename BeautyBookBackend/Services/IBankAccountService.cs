using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public interface IBankAccountService
{
    Task<IReadOnlyList<BankAccountDto>> GetAsync(Guid userId);
    Task<BankAccountOtpResponse?> RequestAddOtpAsync(Guid userId, BankAccountDraftRequest request);
    Task<BankAccountOtpResponse?> RequestUpdateOtpAsync(Guid userId, Guid id, BankAccountDraftRequest request);
    Task<BankAccountDto> AddAsync(Guid userId, UpsertBankAccountRequest request);
    Task<BankAccountDto?> UpdateAsync(Guid userId, Guid id, UpsertBankAccountRequest request);
    Task<BankAccountDto?> SetDefaultAsync(Guid userId, Guid id);
    Task<BankAccountOtpResponse?> RequestDefaultOtpAsync(Guid userId, Guid id) => throw new NotSupportedException();
    Task<BankAccountDto?> SetDefaultWithOtpAsync(Guid userId, Guid id, string otp) => throw new NotSupportedException();
    Task<bool> DeactivateAsync(Guid userId, Guid id);
}
