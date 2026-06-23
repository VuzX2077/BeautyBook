using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Repositories
{
    public class ReviewRepository : IReviewRepository
    {
        private readonly ApplicationDbContext _context;

        public ReviewRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<bool> ExistsForBookingAsync(Guid bookingId)
        {
            return _context.Reviews.AnyAsync(r => r.BookingId == bookingId);
        }

        public Task AddAsync(Review review)
        {
            return _context.Reviews.AddAsync(review).AsTask();
        }

        public Task<List<int>> GetRatingsByMuaIdAsync(Guid muaId)
        {
            return _context.Reviews
                .Where(r => r.MUAId == muaId)
                .Select(r => r.Rating)
                .ToListAsync();
        }

        public Task<List<Review>> GetByMuaIdAsync(Guid muaId)
        {
            return _context.Reviews
                .Where(r => r.MUAId == muaId)
                .Include(r => r.Customer)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }
        public Task<Review?> GetByIdAsync(Guid reviewId)
        {
            return _context.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId);
        }
    }
}
