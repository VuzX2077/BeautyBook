using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services
{
    public interface IMuaReceivableService
    {
        Task<MuaReceivable> EnsureForCompletedBookingAsync(Booking booking);
        Task<MuaReceivable> EnsureForDemoCompletedBookingAsync(Booking booking, Guid initiator);
        Task FreezeForDisputeAsync(Guid bookingId);
        Task RestoreAfterMuaWinsAsync(Guid bookingId);
        Task ReverseAsync(Guid bookingId);
        Task<int> ReconcileStatesAsync();
        Task<MuaEarningsDto> GetEarningsAsync(Guid muaId);
    }
}
