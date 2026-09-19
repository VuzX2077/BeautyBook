using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public interface IRefundService
    {
        Task<Refund> EnsureRefundAsync(Booking booking, BookingPayment payment, decimal amount, RefundReasonCode reasonCode, string reason, Guid? requestedBy);
        Task<RefundSummaryDto?> GetByBookingAsync(Guid bookingId);
        Task<RefundSummaryDto?> StartProcessingAsync(Guid refundId, Guid adminId, string? reference);
        Task<RefundSummaryDto?> CompleteAsync(Guid refundId, Guid adminId, string reference);
        Task<RefundSummaryDto?> FailAsync(Guid refundId, Guid adminId, string failureCode, string failureMessage);
        Task<RefundSummaryDto?> RetryAsync(Guid refundId, Guid adminId);
        Task<int> MovePendingToManualActionRequiredAsync();
    }
}
