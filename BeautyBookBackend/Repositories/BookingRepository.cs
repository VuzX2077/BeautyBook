using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Repositories
{
    public class BookingRepository : IBookingRepository
    {
        private readonly ApplicationDbContext _context;

        public BookingRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task AddAsync(Booking booking)
        {
            return _context.Bookings.AddAsync(booking).AsTask();
        }

        public Task<List<Booking>> GetByUserAsync(Guid userId, string viewAs)
        {
            var query = BookingDetailsQuery();

            if (viewAs.Equals("mua", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => b.MUAId == userId);
            }
            else
            {
                query = query.Where(b => b.CustomerId == userId);
            }

            return query.OrderByDescending(b => b.CreatedAt).ToListAsync();
        }

        public Task<Booking?> GetByIdWithDetailsForUserAsync(Guid bookingId, Guid userId)
        {
            return BookingDetailsQuery()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && (b.CustomerId == userId || b.MUAId == userId));
        }

        public Task<Booking?> GetByIdForParticipantAsync(Guid bookingId, Guid userId)
        {
            return _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId && (b.CustomerId == userId || b.MUAId == userId));
        }

        public Task<Booking?> GetByIdForCustomerAsync(Guid bookingId, Guid customerId)
        {
            return _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == customerId);
        }

        private IQueryable<Booking> BookingDetailsQuery()
        {
            return _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.MakeupArtistProfile)
                    .ThenInclude(m => m!.User)
                .Include(b => b.BookingServices)
                    .ThenInclude(bs => bs.Service);
        }

        public async Task<List<Booking>> GetBookingsByDateAsync(Guid muaId, DateTime date)
        {
            var dateOnly = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var nextDate = dateOnly.AddDays(1);
            return await _context.Bookings
                .Where(b => b.MUAId == muaId && b.BookingDate >= dateOnly && b.BookingDate < nextDate
                    && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Rejected
                    && (b.Status != BookingStatus.PendingPayment
                        || (b.PaymentExpiresAt != null && b.PaymentExpiresAt > DateTime.UtcNow)))
                .ToListAsync();
        }

        public Task<Booking?> GetByIdAsync(Guid bookingId)
        {
            return _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId);
        }

        public Task<bool> HasOverlappingBookingAsync(Guid muaId, DateTime date, TimeSpan startTime, TimeSpan endTime, Guid? excludeBookingId = null)
        {
            var dateOnly = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var nextDate = dateOnly.AddDays(1);

            return _context.Bookings.AnyAsync(b =>
                b.MUAId == muaId
                && b.BookingDate >= dateOnly && b.BookingDate < nextDate
                && b.Status != BookingStatus.Cancelled
                && b.Status != BookingStatus.Rejected
                && (b.Status != BookingStatus.PendingPayment
                    || (b.PaymentExpiresAt != null && b.PaymentExpiresAt > DateTime.UtcNow))
                && (!excludeBookingId.HasValue || b.BookingId != excludeBookingId.Value)
                && startTime < b.EndTime
                && endTime > b.StartTime);
        }

        public Task<List<Booking>> GetOverdueCustomerConfirmationsAsync(DateTime utcNow)
        {
            return _context.Bookings
                .Where(b => b.Status == BookingStatus.WaitingCustomer
                    && b.CustomerConfirmationDeadline != null
                    && b.CustomerConfirmationDeadline <= utcNow)
                .ToListAsync();
        }
    }
}
