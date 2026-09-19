using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public class MuaReceivableService : IMuaReceivableService
    {
        private readonly ApplicationDbContext _context;
        public MuaReceivableService(ApplicationDbContext context) => _context = context;

        public async Task<MuaReceivable> EnsureForCompletedBookingAsync(Booking booking)
        {
            var existing = await _context.MuaReceivables.FirstOrDefaultAsync(x => x.BookingId == booking.BookingId);
            if (existing != null)
            {
                if (existing.Status == MuaReceivableStatus.OnHold)
                {
                    var availableAt = DateTime.UtcNow;
                    existing.Status = MuaReceivableStatus.Available;
                    existing.AvailableAt ??= availableAt;
                    existing.UpdatedAt = availableAt;
                }
                return existing;
            }

            var now = DateTime.UtcNow;
            var receivable = new MuaReceivable
            {
                Id = Guid.NewGuid(), BookingId = booking.BookingId, MuaId = booking.MUAId,
                GrossAmount = booking.DepositAmount, PlatformFeeAmount = booking.PlatformFeeAmount,
                NetAmount = booking.MuaPayoutAmount, Status = MuaReceivableStatus.Available,
                CreatedAt = now, AvailableAt = now, UpdatedAt = now
            };
            await _context.MuaReceivables.AddAsync(receivable);
            return receivable;
        }

        public Task FreezeForDisputeAsync(Guid bookingId) => SetStatusAsync(bookingId, MuaReceivableStatus.Frozen);
        public Task RestoreAfterMuaWinsAsync(Guid bookingId) => SetStatusAsync(bookingId, MuaReceivableStatus.Available);
        public Task ReverseAsync(Guid bookingId) => SetStatusAsync(bookingId, MuaReceivableStatus.Reversed);

        private async Task SetStatusAsync(Guid bookingId, MuaReceivableStatus target)
        {
            var receivable = await _context.MuaReceivables.FirstOrDefaultAsync(x => x.BookingId == bookingId);
            if (receivable == null || receivable.Status is MuaReceivableStatus.PayoutPending or MuaReceivableStatus.PaidOut or MuaReceivableStatus.Reversed)
                return;
            if (receivable.Status == target) return;
            var now = DateTime.UtcNow;
            receivable.Status = target;
            receivable.UpdatedAt = now;
            if (target == MuaReceivableStatus.Frozen) receivable.FrozenAt = now;
            if (target == MuaReceivableStatus.Available)
            {
                receivable.FrozenAt = null;
                receivable.AvailableAt ??= now;
            }
            if (target == MuaReceivableStatus.Reversed) receivable.ReversedAt = now;
        }

        public async Task<int> ReconcileStatesAsync()
        {
            var candidates = await _context.MuaReceivables
                .Where(x => x.Status == MuaReceivableStatus.OnHold || x.Status == MuaReceivableStatus.Frozen)
                .Include(x => x.Booking).ThenInclude(x => x!.Payments)
                .ToListAsync();
            var changed = 0;
            foreach (var item in candidates)
            {
                var refunds = await _context.Refunds.Where(x => x.BookingId == item.BookingId).Select(x => x.Status).ToListAsync();
                var target = refunds.Contains(RefundStatus.Completed)
                    ? MuaReceivableStatus.Reversed
                    : item.Booking!.Status == BookingStatus.Disputed
                      || item.Booking.PaymentStatus is PaymentStatus.Frozen or PaymentStatus.RefundPending
                      || refunds.Any(x => x is RefundStatus.Pending or RefundStatus.ManualActionRequired or RefundStatus.Processing or RefundStatus.Failed)
                        ? MuaReceivableStatus.Frozen
                        : item.Booking.Status is BookingStatus.Completed or BookingStatus.AutoCompleted
                            ? MuaReceivableStatus.Available
                            : MuaReceivableStatus.OnHold;
                if (item.Status == target) continue;
                var now = DateTime.UtcNow; item.Status = target; item.UpdatedAt = now;
                item.FrozenAt = target == MuaReceivableStatus.Frozen ? now : null;
                if (target == MuaReceivableStatus.Available) item.AvailableAt ??= now;
                if (target == MuaReceivableStatus.Reversed) item.ReversedAt = now;
                changed++;
            }
            if (changed > 0) await _context.SaveChangesAsync();
            return changed;
        }

        public async Task<MuaEarningsDto> GetEarningsAsync(Guid muaId)
        {
            var rows = await _context.MuaReceivables.AsNoTracking().Where(x => x.MuaId == muaId)
                .OrderByDescending(x => x.CreatedAt).ToListAsync();
            decimal Total(MuaReceivableStatus status) => rows.Where(x => x.Status == status).Sum(x => x.NetAmount);
            return new MuaEarningsDto
            {
                OnHoldTotal=Total(MuaReceivableStatus.OnHold), AvailableTotal=Total(MuaReceivableStatus.Available),
                FrozenTotal=Total(MuaReceivableStatus.Frozen), PayoutPendingTotal=Total(MuaReceivableStatus.PayoutPending),
                PaidOutTotal=Total(MuaReceivableStatus.PaidOut),
                Receivables=rows.Select(x => new MuaReceivableDto { Id=x.Id, BookingId=x.BookingId,
                    GrossAmount=x.GrossAmount, PlatformFeeAmount=x.PlatformFeeAmount, NetAmount=x.NetAmount,
                    Status=x.Status, CreatedAt=x.CreatedAt, AvailableAt=x.AvailableAt, FrozenAt=x.FrozenAt,
                    PaidOutAt=x.PaidOutAt, ReversedAt=x.ReversedAt }).ToList()
            };
        }
    }
}
