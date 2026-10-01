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
        private readonly IRefundPayoutProvider _payoutProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RefundService> _logger;

        public RefundService(ApplicationDbContext context, IMuaReceivableService receivables,
            IRefundPayoutProvider payoutProvider, IConfiguration configuration, ILogger<RefundService> logger)
        {
            _context = context;
            _receivables = receivables;
            _payoutProvider = payoutProvider;
            _configuration = configuration;
            _logger = logger;
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
            var destination = await _context.BankAccounts.AsNoTracking()
                .Where(BankAccountEligibility.UsableAt(now))
                .Where(x => x.UserId == booking.CustomerId)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .FirstOrDefaultAsync();
            var refund = new Refund
            {
                RefundId = Guid.NewGuid(),
                BookingId = booking.BookingId,
                BookingPaymentId = payment.PaymentId,
                Amount = amount,
                Status = destination == null ? RefundStatus.AwaitingDestination : RefundStatus.Pending,
                ReasonCode = reasonCode,
                Reason = reason,
                RequestedBy = requestedBy,
                CreatedAt = now,
                UpdatedAt = now
            };
            if (destination != null) CaptureDestination(refund, destination, now);
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

        public async Task<RefundSummaryDto?> SetDestinationAsync(Guid refundId, Guid customerId, Guid bankAccountId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var refund = await GetRefundForUpdateAsync(refundId);
            if (refund == null) return null;
            var ownsRefund = await _context.Bookings.AnyAsync(x => x.BookingId == refund.BookingId && x.CustomerId == customerId);
            if (!ownsRefund || refund.Status != RefundStatus.AwaitingDestination) return null;
            var bank = await _context.BankAccounts
                .FromSqlInterpolated($"SELECT * FROM \"BankAccounts\" WHERE \"Id\"={bankAccountId} FOR UPDATE")
                .AsNoTracking()
                .FirstOrDefaultAsync();
            if (bank == null || bank.UserId != customerId) throw BankUnavailable("BANK_ACCOUNT_NOT_FOUND");
            var now = DateTime.UtcNow;
            var reason=BankAccountEligibility.GetUnavailableReason(bank.IsActive,bank.VerificationStatus,bank.ActivatedAt,now);
            if(reason!=null)throw BankUnavailable(reason);
            CaptureDestination(refund, bank, now);
            refund.Status = RefundStatus.Pending;
            refund.UpdatedAt = now;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ToDto(refund);
        }

        public async Task<IReadOnlyList<AdminRefundDto>> GetAdminQueueAsync(RefundStatus? status = null)
        {
            var query = _context.Refunds.AsNoTracking().Include(x => x.Booking).ThenInclude(x => x!.Customer).AsQueryable();
            if (status.HasValue) query = query.Where(x => x.Status == status.Value);
            else query = query.Where(x => x.Status != RefundStatus.Completed);
            return (await query.OrderBy(x => x.CreatedAt).ToListAsync()).Select(ToAdminDto).ToList();
        }

        public async Task<AdminRefundDto?> GetAdminByIdAsync(Guid refundId)
        {
            var refund = await _context.Refunds.AsNoTracking().Include(x => x.Booking).ThenInclude(x => x!.Customer)
                .FirstOrDefaultAsync(x => x.RefundId == refundId);
            return refund == null ? null : ToAdminDto(refund);
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
            var automated = _configuration.GetValue<bool>("Refunds:AutomatedPayoutEnabled");
            var now = DateTime.UtcNow;
            var awaitingIds = await _context.Refunds.AsNoTracking()
                .Where(x => x.Status == RefundStatus.AwaitingDestination)
                .OrderBy(x=>x.CreatedAt)
                .Select(x=>x.RefundId)
                .ToListAsync();
            foreach (var refundId in awaitingIds)
            {
                await using var destinationTransaction=await _context.Database.BeginTransactionAsync();
                var refund=await GetRefundForUpdateAsync(refundId);
                if(refund?.Status!=RefundStatus.AwaitingDestination)continue;
                var customerId=await _context.Bookings.Where(x=>x.BookingId==refund.BookingId).Select(x=>x.CustomerId).FirstOrDefaultAsync();
                var bank=await GetUsableCustomerBankForUpdateAsync(customerId,now);
                if(bank==null){await destinationTransaction.CommitAsync();continue;}
                CaptureDestination(refund,bank,now);
                refund.Status=RefundStatus.Pending;
                refund.UpdatedAt=now;
                await _context.SaveChangesAsync();
                await destinationTransaction.CommitAsync();
            }
            var items = await _context.Refunds.AsNoTracking()
                .Where(x => x.Status == RefundStatus.Pending
                    || (x.Status == RefundStatus.Processing && x.ProviderReferenceId != null))
                .Select(x => new { x.RefundId, x.Status, x.ProviderReferenceId })
                .ToListAsync();
            var count = 0;
            foreach (var item in items)
            {
                // Once a provider operation has started it must always be reconciled,
                // even if the feature flag is switched off during an incident.
                if (automated || (item.Status == RefundStatus.Processing && item.ProviderReferenceId != null))
                {
                    if (await ProcessAutomatedAsync(item.RefundId)) count++;
                    continue;
                }
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var refund = await GetRefundForUpdateAsync(item.RefundId);
                if (refund?.Status != RefundStatus.Pending) continue;
                var customerId=await _context.Bookings.Where(x=>x.BookingId==refund.BookingId).Select(x=>x.CustomerId).FirstOrDefaultAsync();
                if(refund.Status==RefundStatus.Pending&&!await DestinationIsStillUsableAsync(refund,customerId,DateTime.UtcNow))
                {
                    ClearDestination(refund);
                    refund.Status=RefundStatus.AwaitingDestination;
                }
                else refund.Status = RefundStatus.ManualActionRequired;
                refund.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                count++;
            }
            return count;
        }

        private async Task<bool> ProcessAutomatedAsync(Guid refundId)
        {
            RefundPayoutRequest? request = null;
            string? payoutId;
            string referenceId;
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                var refund = await GetRefundForUpdateAsync(refundId);
                if (refund == null || refund.Status is not (RefundStatus.Pending or RefundStatus.Processing)) return false;
                var customerId=await _context.Bookings.Where(x=>x.BookingId==refund.BookingId).Select(x=>x.CustomerId).FirstOrDefaultAsync();
                if(refund.Status==RefundStatus.Pending&&!await DestinationIsStillUsableAsync(refund,customerId,DateTime.UtcNow))
                {
                    ClearDestination(refund);
                    refund.Status=RefundStatus.AwaitingDestination;
                    refund.UpdatedAt=DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                if (string.IsNullOrWhiteSpace(refund.DestinationBankBin)
                    || string.IsNullOrWhiteSpace(refund.DestinationAccountNumber)
                    || string.IsNullOrWhiteSpace(refund.DestinationAccountName))
                {
                    refund.Status = RefundStatus.AwaitingDestination;
                    refund.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }

                referenceId = refund.ProviderReferenceId ?? $"refund_{refund.RefundId:N}";
                refund.ProviderReferenceId = referenceId;
                refund.Status = RefundStatus.Processing;
                refund.ProcessingAt ??= DateTime.UtcNow;
                refund.LastAttemptAt = DateTime.UtcNow;
                refund.AttemptCount += 1;
                refund.UpdatedAt = DateTime.UtcNow;
                payoutId = refund.ProviderPayoutId;
                if (payoutId == null)
                    request = new RefundPayoutRequest(referenceId, decimal.ToInt64(refund.Amount),
                        $"Hoan tien {refund.BookingId:N}"[..Math.Min(25, $"Hoan tien {refund.BookingId:N}".Length)],
                        refund.DestinationBankBin, refund.DestinationAccountNumber);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            RefundPayoutResult result;
            try
            {
                result = payoutId == null
                    ? await _payoutProvider.CreateAsync(request!, referenceId)
                    : await _payoutProvider.GetAsync(payoutId);
            }
            catch (Exception ex)
            {
                // The provider may have accepted the idempotent request even when the response was lost.
                // Keep Processing and retry with the same reference/idempotency key on the next pass.
                _logger.LogError(ex, "Unable to reconcile automated refund {RefundId}.", refundId);
                return false;
            }

            await using var finalizeTransaction = await _context.Database.BeginTransactionAsync();
            var current = await GetRefundForUpdateAsync(refundId);
            if (current == null || current.Status != RefundStatus.Processing) return false;
            current.ProviderPayoutId = result.PayoutId;
            current.LastProviderState = result.State;
            current.ProviderReference = result.TransactionReference ?? current.ProviderReference;
            current.UpdatedAt = DateTime.UtcNow;
            if (result.Failed)
            {
                RefundLifecycle.MarkFailed(current, DateTime.UtcNow,
                    result.FailureCode ?? "PAYOS_PAYOUT_FAILED", result.FailureMessage ?? $"payOS payout state: {result.State}");
            }
            else if (result.Completed)
            {
                var booking = await _context.Bookings
                    .FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\" = {current.BookingId} FOR UPDATE")
                    .FirstAsync();
                var payment = await _context.BookingPayments
                    .FromSqlInterpolated($"SELECT * FROM \"BookingPayments\" WHERE \"PaymentId\" = {current.BookingPaymentId} FOR UPDATE")
                    .FirstAsync();
                current.Status = RefundStatus.Completed;
                current.CompletedAt = DateTime.UtcNow;
                payment.RefundedAt = current.CompletedAt;
                payment.UpdatedAt = current.CompletedAt.Value;
                var full = current.Amount >= payment.Amount;
                payment.Status = full ? BookingPaymentStatus.Refunded : BookingPaymentStatus.PartiallyRefunded;
                booking.PaymentStatus = full ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
                booking.UpdatedAt = current.CompletedAt.Value;
                await ApplyComplaintRefundAsync(booking, current, payment.Amount);
            }
            await _context.SaveChangesAsync();
            await finalizeTransaction.CommitAsync();
            return true;
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

            // Provider-managed refunds are reconciled only by the automated worker.
            // Mixing a manual completion with an in-flight idempotent payout can double-pay the customer.
            if (!string.IsNullOrWhiteSpace(refund.ProviderReferenceId)
                && !(target == RefundStatus.ManualActionRequired && refund.Status == RefundStatus.Failed)) return null;

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
            if (target == RefundStatus.Processing
                && (string.IsNullOrWhiteSpace(refund.DestinationBankBin)
                    || string.IsNullOrWhiteSpace(refund.DestinationAccountNumber)
                    || string.IsNullOrWhiteSpace(refund.DestinationAccountName)))
                return null;
            if(target==RefundStatus.Processing&&!await DestinationIsStillUsableAsync(refund,booking.CustomerId,DateTime.UtcNow))
            {
                ClearDestination(refund);
                refund.Status=RefundStatus.AwaitingDestination;
                refund.UpdatedAt=DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return null;
            }
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
                await ApplyComplaintRefundAsync(booking, refund, payment.Amount);
                _context.AppNotifications.Add(new AppNotification { Id = Guid.NewGuid(), UserId = booking.CustomerId, Type = "REFUND_COMPLETED", Title = "Hoàn tiền thành công", Body = $"B-Book đã xác nhận hoàn {refund.Amount:N0}đ. Mã giao dịch: {refund.ProviderReference}.", DataJson = System.Text.Json.JsonSerializer.Serialize(new { url = $"/booking/{booking.BookingId}/cancel-success" }), ScheduledAt = now, Status = "Pending", CreatedAt = now });
            }
            else if (target == RefundStatus.Failed)
            {
                RefundLifecycle.MarkFailed(refund, now, failureCode, failureMessage);
            }
            else if (target == RefundStatus.ManualActionRequired)
            {
                // A provider-terminal failure may fall back to manual transfer. Keep the
                // provider payout id/state for audit but detach it from future processing.
                refund.ProviderReferenceId = null;
                refund.ProcessingAt = null;
                refund.FailedAt = null;
                refund.FailureCode = null;
                refund.FailureMessage = null;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ToDto(refund);
        }

        private async Task ApplyComplaintRefundAsync(Booking booking, Refund refund, decimal paidAmount)
        {
            var complaint = await _context.BookingComplaints.FirstOrDefaultAsync(c => c.RefundId == refund.RefundId);
            if (complaint == null || refund.Amount >= paidAmount) {
                await _receivables.ReverseAsync(booking.BookingId);
                return;
            }
            // Derive the remaining ledger from the original booking snapshot: retry-safe.
            var r = await _context.MuaReceivables.FirstOrDefaultAsync(r => r.BookingId == booking.BookingId);
            if (r == null) {
                r = new MuaReceivable { Id = Guid.NewGuid(), BookingId = booking.BookingId, MuaId = booking.MUAId, CreatedAt = DateTime.UtcNow };
                _context.MuaReceivables.Add(r);
            }
            var remainingRatio = (paidAmount - refund.Amount) / paidAmount;
            r.GrossAmount = paidAmount - refund.Amount;
            r.NetAmount = decimal.Round(booking.MuaPayoutAmount * remainingRatio, 0, MidpointRounding.AwayFromZero);
            r.PlatformFeeAmount = r.GrossAmount - r.NetAmount;
            r.Status = MuaReceivableStatus.OnHold;
            r.FrozenAt = null; r.UpdatedAt = DateTime.UtcNow;
            r.AvailableAt = booking.CompletedAt?.AddHours(ComplaintPolicy.WindowHours);
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
            MaskedDestinationAccountNumber = refund.DestinationAccountNumber == null ? null : Mask(refund.DestinationAccountNumber),
            DestinationBankName = refund.DestinationBankName,
            DestinationAccountName = refund.DestinationAccountName,
            CreatedAt = refund.CreatedAt,
            ProcessingAt = refund.ProcessingAt,
            CompletedAt = refund.CompletedAt,
            FailedAt = refund.FailedAt,
            FailureCode = refund.FailureCode,
            FailureMessage = refund.FailureMessage
        };

        private async Task<BankAccount?> GetUsableCustomerBankForUpdateAsync(Guid customerId,DateTime now)
        {
            var candidateId=await _context.BankAccounts.AsNoTracking()
                .Where(BankAccountEligibility.UsableAt(now))
                .Where(x=>x.UserId==customerId)
                .OrderByDescending(x=>x.IsDefault)
                .ThenBy(x=>x.CreatedAt)
                .ThenBy(x=>x.Id)
                .Select(x=>x.Id)
                .FirstOrDefaultAsync();
            if(candidateId==Guid.Empty)return null;
            var bank=await _context.BankAccounts.FromSqlInterpolated($"SELECT * FROM \"BankAccounts\" WHERE \"Id\"={candidateId} FOR UPDATE").AsNoTracking().FirstOrDefaultAsync();
            return bank!=null&&bank.UserId==customerId&&BankAccountEligibility.IsUsable(bank,now)?bank:null;
        }

        private async Task<bool> DestinationIsStillUsableAsync(Refund refund,Guid customerId,DateTime now)
        {
            if(!refund.DestinationBankAccountId.HasValue)return false;
            var bank=await _context.BankAccounts.FromSqlInterpolated($"SELECT * FROM \"BankAccounts\" WHERE \"Id\"={refund.DestinationBankAccountId.Value} FOR UPDATE").AsNoTracking().FirstOrDefaultAsync();
            return bank!=null&&bank.UserId==customerId&&BankAccountEligibility.IsUsable(bank,now)&&BankAccountEligibility.SnapshotMatches(refund,bank);
        }

        private static void ClearDestination(Refund refund)
        {
            refund.DestinationBankAccountId=null;
            refund.DestinationBankBin=null;
            refund.DestinationBankCode=null;
            refund.DestinationBankName=null;
            refund.DestinationAccountNumber=null;
            refund.DestinationAccountName=null;
            refund.DestinationQrCodeUrl=null;
            refund.DestinationCapturedAt=null;
        }

        private static void CaptureDestination(Refund refund, BankAccount bank, DateTime now)
        {
            refund.DestinationBankAccountId = bank.Id;
            refund.DestinationBankBin = bank.BankBin;
            refund.DestinationBankCode = bank.BankCode;
            refund.DestinationBankName = bank.BankName;
            refund.DestinationAccountNumber = bank.AccountNumber;
            refund.DestinationAccountName = bank.AccountHolderName;
            refund.DestinationQrCodeUrl = bank.QrCodeUrl;
            refund.DestinationCapturedAt = now;
        }

        private static string Mask(string value) => value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
        private static BookingRuleException BankUnavailable(string code)=>new(code,code switch{"BANK_ACCOUNT_PENDING_APPROVAL"=>"Tài khoản nhận tiền đang chờ admin duyệt.","BANK_ACCOUNT_REJECTED"=>"Tài khoản nhận tiền đã bị từ chối.",_=>"Không tìm thấy tài khoản nhận tiền đang hoạt động."},409);

        private static AdminRefundDto ToAdminDto(Refund refund) => new()
        {
            RefundId = refund.RefundId, BookingId = refund.BookingId,
            CustomerId = refund.Booking?.CustomerId ?? Guid.Empty, CustomerName = refund.Booking?.Customer?.FullName,
            Amount = refund.Amount, Status = refund.Status, ReasonCode = refund.ReasonCode, Reason = refund.Reason,
            ProviderReference = refund.ProviderReference, ProviderPayoutId = refund.ProviderPayoutId,
            LastProviderState = refund.LastProviderState, AttemptCount = refund.AttemptCount,
            DestinationBankBin = refund.DestinationBankBin, DestinationBankCode = refund.DestinationBankCode, DestinationBankName = refund.DestinationBankName,
            DestinationAccountNumber = refund.DestinationAccountNumber,
            DestinationQrCodeUrl = refund.DestinationQrCodeUrl,
            MaskedDestinationAccountNumber = refund.DestinationAccountNumber == null ? null : Mask(refund.DestinationAccountNumber),
            DestinationAccountName = refund.DestinationAccountName, CreatedAt = refund.CreatedAt,
            ProcessingAt = refund.ProcessingAt, CompletedAt = refund.CompletedAt, FailedAt = refund.FailedAt,
            FailureCode = refund.FailureCode, FailureMessage = refund.FailureMessage
        };
    }
}
