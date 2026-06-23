using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Repositories
{
    public interface IReviewRepository
    {
        Task<bool> ExistsForBookingAsync(Guid bookingId);
        Task AddAsync(Review review);
        Task<List<int>> GetRatingsByMuaIdAsync(Guid muaId);
        Task<List<Review>> GetByMuaIdAsync(Guid muaId);
        Task<Review?> GetByIdAsync(Guid reviewId);
    }
}
