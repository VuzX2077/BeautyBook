using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IPayoutService
    {
        Task<IReadOnlyList<MuaBankAccountDto>> GetBankAccountsAsync(Guid muaId);
        Task<MuaBankAccountDto> AddBankAccountAsync(Guid muaId, UpsertMuaBankAccountRequest request);
        Task<MuaBankAccountDto?> UpdateBankAccountAsync(Guid muaId, Guid id, UpsertMuaBankAccountRequest request);
        Task<bool> DeactivateBankAccountAsync(Guid muaId, Guid id);
        Task<PayoutDto> CreateAsync(Guid muaId, CreatePayoutRequest request);
        Task<IReadOnlyList<PayoutDto>> GetOwnAsync(Guid muaId);
        Task<IReadOnlyList<AdminPayoutDto>> GetPendingAdminAsync();
        Task<PayoutDto?> StartProcessingAsync(Guid payoutId, Guid adminId, string? reference);
        Task<PayoutDto?> CompleteAsync(Guid payoutId, Guid adminId, string reference);
        Task<PayoutDto?> FailAsync(Guid payoutId, Guid adminId, string code, string message, bool confirmedFundsNotSent);
        Task<int> MovePendingToManualActionRequiredAsync();
        Task<bool> HasPendingForBookingAsync(Guid bookingId);
    }
}
