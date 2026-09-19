using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public class RefundService : IRefundService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMuaReceivableService _receivables;

        public RefundService(ApplicationDbContext context, IMuaReceivableService receivables)
        {
            _context = context;
            _receivables = receivables;
        }

        public async Task<Refund> EnsureRefundAsync(
            Booking booking,
            BookingPayment payment,
            decimal amount,
            RefundReasonCode reasonCode,
            string reason,
            Guid? requestedBy)
        {
            if (amount <= 0 || amount > payment.Amount)
                throw new BookingRuleException("INVALID_REFUND_AMOUNT", "Số tiền hoàn phải lớn hơn 0 và không vượt quá số tiền đã thanh toán.");

            var existing = await _context.Refunds.FirstOrDefaultAsync(x => x.BookingPaymentId == payment.PaymentId);
            if (existing != null)
            {
                if (existing.Amount != amount)
                    throw new BookingRuleException("REFUND_IDEMPOTENCY_CONFLICT", "Khoản thanh toán đã có yêu cầu hoàn tiền với số tiền khác.", 409);
                return existing;
            }

            var now = DateTime.UtcNow;
            var refund = new Refund
            {
                RefundId = Guid.NewGuid(),
                BookingId = booking.BookingId,
                BookingPaymentId = payment.PaymentId,
                Amount = amount,
                Status = RefundStatus.Pending,
                ReasonCode = reasonCode,
                Reason = reason,
                RequestedBy = requestedBy,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _context.Refunds.AddAsync(refund);
            return refund;
        }

        public async Task<RefundSummaryDto?> GetByBookingAsync(Guid bookingId)
        {
            var refund = await _context.Refunds.AsNoTracking()
                .Where(x => x.BookingId == bookingId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
            return refund == null ? null : ToDto(refund);
        }

        public Task<RefundSummaryDto?> StartProcessingAsync(Guid refundId, Guid adminId, string? reference) =>
            TransitionAsync(refundId, adminId, RefundStatus.Processing, reference, null, null);

        public Task<RefundSummaryDto?> CompleteAsync(Guid refundId, Guid adminId, string reference) =>
            TransitionAsync(refundId, adminId, RefundStatus.Completed, reference, null, null);

        public Task<RefundSummaryDto?> FailAsync(Guid refundId, Guid adminId, string failureCode, string failureMessage) =>
            TransitionAsync(refundId, adminId, RefundStatus.Failed, null, failureCode, failureMessage);

        public Task<RefundSummaryDto?> RetryAsync(Guid refundId, Guid adminId) =>
            TransitionAsync(refundId, adminId, RefundStatus.ManualActionRequired, null, null, null);

        public async Task<int> MovePendingToManualActionRequiredAsync()
        {
            var ids = await _context.Refunds.AsNoTracking()
                .Where(x => x.Status == RefundStatus.Pending)
                .Select(x => x.RefundId)
                .ToListAsync();
            var count = 0;
            foreach (var id in ids)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var refund = await GetRefundForUpdateAsync(id);
                if (refund?.Status != RefundStatus.Pending) continue;
                refund.Status = RefundStatus.ManualActionRequired;
                refund.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                count++;
            }
            return count;
        }

        private async Task<RefundSummaryDto?> TransitionAsync(
            Guid refundId,
            Guid adminId,
            RefundStatus target,
            string? reference,
            string? failureCode,
            string? failureMessage)
        {
            var identity = await _context.Refunds.AsNoTracking()
                .Where(x => x.RefundId == refundId)
                .Select(x => new { x.RefundId, x.BookingId, x.BookingPaymentId })
                .FirstOrDefaultAsync();
            if (identity == null) return null;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var booking = await _context.Bookings
                .FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\" = {identity.BookingId} FOR UPDATE")
                .FirstOrDefaultAsync();
            var payment = await _context.BookingPayments
                .FromSqlInterpolated($"SELECT * FROM \"BookingPayments\" WHERE \"PaymentId\" = {identity.BookingPaymentId} FOR UPDATE")
                .FirstOrDefaultAsync();
            var refund = await GetRefundForUpdateAsync(identity.RefundId);
            if (booking == null || payment == null || refund == null) return null;

            if (refund.Status == target)
            {
                await transaction.CommitAsync();
                return ToDto(refund);
            }

            var allowed = target switch
            {
                RefundStatus.Processing => refund.Status is RefundStatus.Pending or RefundStatus.ManualActionRequired,
                RefundStatus.Completed => refund.Status == RefundStatus.Processing && !string.IsNullOrWhiteSpace(reference),
                RefundStatus.Failed => refund.Status == RefundStatus.Processing,
                RefundStatus.ManualActionRequired => refund.Status == RefundStatus.Failed,
                _ => false
            };
            if (!allowed) return null;
            if (target == RefundStatus.Completed && await _context.PayoutItems.AnyAsync(x => x.IsActive
                && x.MuaReceivable!.BookingId == booking.BookingId
                && (x.MuaReceivable.Status == MuaReceivableStatus.PayoutPending
                    || x.MuaReceivable.Status == MuaReceivableStatus.PaidOut)))
                return null;

            var now = DateTime.UtcNow;
            refund.Status = target;
            refund.LastHandledBy = adminId;
            refund.UpdatedAt = now;
            if (!string.IsNullOrWhiteSpace(reference)) refund.ProviderReference = reference.Trim();

            if (target == RefundStatus.Processing)
            {
                refund.ProcessingAt = now;
                refund.FailedAt = null;
                refund.FailureCode = null;
                refund.FailureMessage = null;
            }
            else if (target == RefundStatus.Completed)
            {
                refund.CompletedAt = now;
                payment.RefundedAt = now;
                payment.UpdatedAt = now;
                var isFullRefund = refund.Amount >= payment.Amount;
                payment.Status = isFullRefund ? BookingPaymentStatus.Refunded : BookingPaymentStatus.PartiallyRefunded;
                booking.PaymentStatus = isFullRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
                booking.UpdatedAt = now;
                await _receivables.ReverseAsync(booking.BookingId);
            }
            else if (target == RefundStatus.Failed)
            {
                RefundLifecycle.MarkFailed(refund, now, failureCode, failureMessage);
            }
            else if (target == RefundStatus.ManualActionRequired)
            {
                refund.ProcessingAt = null;
                refund.FailedAt = null;
                refund.FailureCode = null;
                refund.FailureMessage = null;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ToDto(refund);
        }

        private Task<Refund?> GetRefundForUpdateAsync(Guid refundId) =>
            _context.Refunds
                .FromSqlInterpolated($"SELECT * FROM \"Refunds\" WHERE \"RefundId\" = {refundId} FOR UPDATE")
                .FirstOrDefaultAsync();

        private static RefundSummaryDto ToDto(Refund refund) => new()
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            ReasonCode = refund.ReasonCode,
            Reason = refund.Reason,
            ProviderReference = refund.ProviderReference,
            CreatedAt = refund.CreatedAt,
            ProcessingAt = refund.ProcessingAt,
            CompletedAt = refund.CompletedAt,
            FailedAt = refund.FailedAt,
            FailureCode = refund.FailureCode,
            FailureMessage = refund.FailureMessage
        };
    }
}
