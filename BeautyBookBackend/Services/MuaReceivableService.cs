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
            var now = DateTime.UtcNow;
            if (!booking.CompletedAt.HasValue)
                throw new InvalidOperationException("Booking chưa có thời điểm hoàn thành.");
            var blocked = await _context.BookingComplaints.AnyAsync(c => c.BookingId == booking.BookingId && c.IsOpen)
                || await _context.Refunds.AnyAsync(r => r.BookingId == booking.BookingId && r.Status != RefundStatus.Completed);
            var existing = await _context.MuaReceivables.FirstOrDefaultAsync(x => x.BookingId == booking.BookingId);
            if (existing != null)
            {
                // Never reopen committed, paid, reversed or explicitly frozen funds.
                if (existing.Status == MuaReceivableStatus.OnHold)
                {
                    existing.Status = blocked ? MuaReceivableStatus.Frozen
                        : existing.NetAmount <= 0 ? MuaReceivableStatus.Reversed : MuaReceivableStatus.Available;
                    existing.AvailableAt = booking.CompletedAt;
                    existing.UpdatedAt = now;
                }
                return existing;
            }
            var receivable = new MuaReceivable
            {
                Id = Guid.NewGuid(), BookingId = booking.BookingId, MuaId = booking.MUAId,
                GrossAmount = booking.DepositAmount, PlatformFeeAmount = booking.PlatformFeeAmount,
                NetAmount = booking.MuaPayoutAmount, Status = blocked ? MuaReceivableStatus.Frozen
                    : booking.MuaPayoutAmount <= 0 ? MuaReceivableStatus.Reversed : MuaReceivableStatus.Available,
                CreatedAt = now, AvailableAt = booking.CompletedAt, UpdatedAt = now
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
                var booking = await _context.Bookings.FindAsync(bookingId);
                var blocked = await _context.BookingComplaints.AnyAsync(c => c.BookingId == bookingId && c.IsOpen)
                    || await _context.Refunds.AnyAsync(r => r.BookingId == bookingId && r.Status != RefundStatus.Completed);
                if (blocked || booking?.PaymentStatus is PaymentStatus.Frozen or PaymentStatus.RefundPending)
                    receivable.Status = MuaReceivableStatus.Frozen;
                else if (booking == null || booking.Status is not (BookingStatus.Completed or BookingStatus.AutoCompleted) || ComplaintPolicy.HasHold(booking, now))
                    receivable.Status = MuaReceivableStatus.OnHold;
                receivable.FrozenAt = null;
                receivable.AvailableAt = booking?.CompletedAt ?? receivable.AvailableAt;
            }
            if (target == MuaReceivableStatus.Reversed) receivable.ReversedAt = now;
        }

        public async Task<int> ReconcileStatesAsync()
        {
            var candidates = await _context.MuaReceivables.AsNoTracking()
                .Where(x => (x.Status == MuaReceivableStatus.OnHold || x.Status == MuaReceivableStatus.Frozen)
                    && _context.Users.Any(u => u.UserId == x.MuaId && u.DeletedAt == null && u.IsActive))
                .Select(x => new { x.Id, x.BookingId })
                .ToListAsync();
            var changed = 0;
            foreach (var candidate in candidates)
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                var booking = await _context.Bookings.FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\"={candidate.BookingId} FOR UPDATE").FirstAsync();
                var item = await _context.MuaReceivables.FromSqlInterpolated($"SELECT * FROM \"MuaReceivables\" WHERE \"Id\"={candidate.Id} FOR UPDATE").FirstAsync();
                // A payout may have claimed the row since the initial scan.
                if (item.Status is not (MuaReceivableStatus.OnHold or MuaReceivableStatus.Frozen)) { await tx.CommitAsync(); continue; }
                // Recheck after taking the booking lock; a deletion may follow the initial scan.
                if (!await _context.Users.AnyAsync(u => u.UserId == item.MuaId && u.DeletedAt == null && u.IsActive))
                { await tx.CommitAsync(); continue; }
                item.Booking = booking;
                var refunds = await _context.Refunds.Where(x => x.BookingId == item.BookingId).Select(x => x.Status).ToListAsync();
                var openComplaint = await _context.BookingComplaints.AnyAsync(c => c.BookingId == item.BookingId && c.IsOpen);
                var target = item.NetAmount <= 0 || (refunds.Contains(RefundStatus.Completed) && item.Booking!.PaymentStatus == PaymentStatus.Refunded)
                    ? MuaReceivableStatus.Reversed
                    : openComplaint || item.Booking!.Status == BookingStatus.Disputed
                      || item.Booking.PaymentStatus is PaymentStatus.Frozen or PaymentStatus.RefundPending
                      || refunds.Any(x => x != RefundStatus.Completed)
                        ? MuaReceivableStatus.Frozen
                        : item.Booking.Status is BookingStatus.Completed or BookingStatus.AutoCompleted
                          && !ComplaintPolicy.HasHold(item.Booking, DateTime.UtcNow)
                            ? MuaReceivableStatus.Available
                            : MuaReceivableStatus.OnHold;
                if (item.Status == target) { await tx.CommitAsync(); continue; }
                var now = DateTime.UtcNow; item.Status = target; item.UpdatedAt = now;
                item.FrozenAt = target == MuaReceivableStatus.Frozen ? now : null;
                if (target == MuaReceivableStatus.Available) item.AvailableAt = booking.CompletedAt ?? now;
                if (target == MuaReceivableStatus.Reversed) item.ReversedAt = now;
                changed++;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
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
