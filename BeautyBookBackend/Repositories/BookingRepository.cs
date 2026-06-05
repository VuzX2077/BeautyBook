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

        public Task<List<Booking>> GetByUserAsync(Guid userId, UserRole role)
        {
            var query = BookingDetailsQuery();

            if (role == UserRole.MUA)
            {
                query = query.Where(b => b.MUAId == userId);
            }
            else if (role == UserRole.Customer)
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
                .Include(b => b.Service);
        }
    }
}
