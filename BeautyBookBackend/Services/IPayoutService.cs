using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IPayoutService
    {
        Task<PayoutDto> CreateAsync(Guid muaId, CreatePayoutRequest request);
        Task<IReadOnlyList<PayoutDto>> GetOwnAsync(Guid muaId);
        Task<PayoutDto?> GetOwnByIdAsync(Guid muaId, Guid payoutId);
        Task<IReadOnlyList<PayoutDto>> GetPendingAdminAsync();
        Task<AdminPayoutDto?> GetAdminByIdAsync(Guid payoutId);
        Task<PayoutDto?> StartProcessingAsync(Guid payoutId, Guid adminId, string? reference);
        Task<PayoutDto?> CompleteAsync(Guid payoutId, Guid adminId, string reference);
        Task<PayoutDto?> FailAsync(Guid payoutId, Guid adminId, string code, string message, bool confirmedFundsNotSent);
        Task<int> MovePendingToManualActionRequiredAsync();
        Task<bool> HasPendingForBookingAsync(Guid bookingId);
    }
}
