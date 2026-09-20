using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public interface IRefundService
    {
        Task<Refund> EnsureRefundAsync(Booking booking, BookingPayment payment, decimal amount, RefundReasonCode reasonCode, string reason, Guid? requestedBy);
        Task<RefundSummaryDto?> GetByBookingAsync(Guid bookingId);
        Task<IReadOnlyList<CustomerBankAccountDto>> GetBankAccountsAsync(Guid customerId);
        Task<CustomerBankAccountDto> AddBankAccountAsync(Guid customerId, UpsertCustomerBankAccountRequest request);
        Task<CustomerBankAccountDto?> UpdateBankAccountAsync(Guid customerId, Guid id, UpsertCustomerBankAccountRequest request);
        Task<bool> DeactivateBankAccountAsync(Guid customerId, Guid id);
        Task<RefundSummaryDto?> SetDestinationAsync(Guid refundId, Guid customerId, Guid bankAccountId);
        Task<IReadOnlyList<AdminRefundDto>> GetAdminQueueAsync(RefundStatus? status = null);
        Task<AdminRefundDto?> GetAdminByIdAsync(Guid refundId);
        Task<RefundSummaryDto?> StartProcessingAsync(Guid refundId, Guid adminId, string? reference);
        Task<RefundSummaryDto?> CompleteAsync(Guid refundId, Guid adminId, string reference);
        Task<RefundSummaryDto?> FailAsync(Guid refundId, Guid adminId, string failureCode, string failureMessage);
        Task<RefundSummaryDto?> RetryAsync(Guid refundId, Guid adminId);
        Task<int> MovePendingToManualActionRequiredAsync();
    }
}
