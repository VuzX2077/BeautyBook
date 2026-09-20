using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

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
            var destination = await _context.CustomerBankAccounts.AsNoTracking()
                .Where(x => x.CustomerId == booking.CustomerId && x.IsActive && x.IsDefault)
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

        public async Task<IReadOnlyList<CustomerBankAccountDto>> GetBankAccountsAsync(Guid customerId) =>
            (await _context.CustomerBankAccounts.AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.IsActive)
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAt)
                .ToListAsync()).Select(ToBankDto).ToList();

        public async Task<CustomerBankAccountDto> AddBankAccountAsync(Guid customerId, UpsertCustomerBankAccountRequest request)
        {
            ValidateBank(request);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var lockKey = "customer-bank:" + customerId.ToString("N");
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var hasAny = await _context.CustomerBankAccounts.AnyAsync(x => x.CustomerId == customerId && x.IsActive);
            if (request.IsDefault || !hasAny) await ClearDefaultsAsync(customerId);
            var now = DateTime.UtcNow;
            var entity = new CustomerBankAccount
            {
                Id = Guid.NewGuid(), CustomerId = customerId, BankBin = request.BankBin.Trim(),
                BankName = Clean(request.BankName), AccountNumber = request.AccountNumber.Trim(),
                AccountHolderName = request.AccountHolderName.Trim().ToUpperInvariant(),
                IsDefault = request.IsDefault || !hasAny, IsActive = true, CreatedAt = now, UpdatedAt = now
            };
            _context.CustomerBankAccounts.Add(entity);
            await _context.SaveChangesAsync();
            if (entity.IsDefault)
            {
                await AttachAwaitingRefundsAsync(customerId, entity, now);
                await _context.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return ToBankDto(entity);
        }

        public async Task<CustomerBankAccountDto?> UpdateBankAccountAsync(Guid customerId, Guid id, UpsertCustomerBankAccountRequest request)
        {
            ValidateBank(request);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var entity = await _context.CustomerBankAccounts
                .FromSqlInterpolated($"SELECT * FROM \"CustomerBankAccounts\" WHERE \"Id\"={id} FOR UPDATE")
                .FirstOrDefaultAsync();
            if (entity == null || entity.CustomerId != customerId || !entity.IsActive) return null;
            if (request.IsDefault) await ClearDefaultsAsync(customerId);
            entity.BankBin = request.BankBin.Trim();
            entity.BankName = Clean(request.BankName);
            entity.AccountNumber = request.AccountNumber.Trim();
            entity.AccountHolderName = request.AccountHolderName.Trim().ToUpperInvariant();
            entity.IsDefault = request.IsDefault;
            var now = DateTime.UtcNow;
            entity.UpdatedAt = now;
            await _context.SaveChangesAsync();
            if (entity.IsDefault)
            {
                await AttachAwaitingRefundsAsync(customerId, entity, now);
                await _context.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return ToBankDto(entity);
        }

        public async Task<bool> DeactivateBankAccountAsync(Guid customerId, Guid id)
        {
            var entity = await _context.CustomerBankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId && x.IsActive);
            if (entity == null) return false;
            entity.IsActive = false;
            entity.IsDefault = false;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<RefundSummaryDto?> SetDestinationAsync(Guid refundId, Guid customerId, Guid bankAccountId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var refund = await GetRefundForUpdateAsync(refundId);
            if (refund == null) return null;
            var ownsRefund = await _context.Bookings.AnyAsync(x => x.BookingId == refund.BookingId && x.CustomerId == customerId);
            if (!ownsRefund || refund.Status != RefundStatus.AwaitingDestination) return null;
            var bank = await _context.CustomerBankAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == bankAccountId && x.CustomerId == customerId && x.IsActive);
            if (bank == null) return null;
            CaptureDestination(refund, bank, DateTime.UtcNow);
            refund.Status = RefundStatus.Pending;
            refund.UpdatedAt = DateTime.UtcNow;
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
                refund.Status = string.IsNullOrWhiteSpace(refund.DestinationAccountNumber)
                    ? RefundStatus.AwaitingDestination
                    : RefundStatus.ManualActionRequired;
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
                await _receivables.ReverseAsync(booking.BookingId);
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

        private Task ClearDefaultsAsync(Guid customerId) => _context.CustomerBankAccounts
            .Where(x => x.CustomerId == customerId && x.IsDefault)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDefault, false));

        private async Task AttachAwaitingRefundsAsync(Guid customerId, CustomerBankAccount bank, DateTime now)
        {
            var refunds = await _context.Refunds
                .Where(x => x.Status == RefundStatus.AwaitingDestination
                    && _context.Bookings.Any(b => b.BookingId == x.BookingId && b.CustomerId == customerId))
                .ToListAsync();

            foreach (var refund in refunds)
            {
                CaptureDestination(refund, bank, now);
                refund.Status = RefundStatus.Pending;
                refund.UpdatedAt = now;
            }
        }

        private static void CaptureDestination(Refund refund, CustomerBankAccount bank, DateTime now)
        {
            refund.DestinationBankBin = bank.BankBin;
            refund.DestinationBankName = bank.BankName;
            refund.DestinationAccountNumber = bank.AccountNumber;
            refund.DestinationAccountName = bank.AccountHolderName;
            refund.DestinationCapturedAt = now;
        }

        private static void ValidateBank(UpsertCustomerBankAccountRequest request)
        {
            if (!Regex.IsMatch(request.BankBin?.Trim() ?? string.Empty, "^[0-9]{6}$"))
                throw new BookingRuleException("INVALID_BANK_BIN", "Mã BIN ngân hàng không hợp lệ.");
            if (!Regex.IsMatch(request.AccountNumber?.Trim() ?? string.Empty, "^[A-Za-z0-9]{5,30}$"))
                throw new BookingRuleException("INVALID_BANK_ACCOUNT", "Số tài khoản không hợp lệ.");
            if (string.IsNullOrWhiteSpace(request.AccountHolderName))
                throw new BookingRuleException("INVALID_ACCOUNT_HOLDER", "Tên chủ tài khoản là bắt buộc.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Mask(string value) => value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
        private static CustomerBankAccountDto ToBankDto(CustomerBankAccount bank) => new()
        {
            Id = bank.Id, BankBin = bank.BankBin, BankName = bank.BankName,
            MaskedAccountNumber = Mask(bank.AccountNumber), AccountHolderName = bank.AccountHolderName,
            IsDefault = bank.IsDefault
        };

        private static AdminRefundDto ToAdminDto(Refund refund) => new()
        {
            RefundId = refund.RefundId, BookingId = refund.BookingId,
            CustomerId = refund.Booking?.CustomerId ?? Guid.Empty, CustomerName = refund.Booking?.Customer?.FullName,
            Amount = refund.Amount, Status = refund.Status, ReasonCode = refund.ReasonCode, Reason = refund.Reason,
            ProviderReference = refund.ProviderReference, ProviderPayoutId = refund.ProviderPayoutId,
            LastProviderState = refund.LastProviderState, AttemptCount = refund.AttemptCount,
            DestinationBankBin = refund.DestinationBankBin, DestinationBankName = refund.DestinationBankName,
            DestinationAccountNumber = refund.DestinationAccountNumber,
            MaskedDestinationAccountNumber = refund.DestinationAccountNumber == null ? null : Mask(refund.DestinationAccountNumber),
            DestinationAccountName = refund.DestinationAccountName, CreatedAt = refund.CreatedAt,
            ProcessingAt = refund.ProcessingAt, CompletedAt = refund.CompletedAt, FailedAt = refund.FailedAt,
            FailureCode = refund.FailureCode, FailureMessage = refund.FailureMessage
        };
    }
}
