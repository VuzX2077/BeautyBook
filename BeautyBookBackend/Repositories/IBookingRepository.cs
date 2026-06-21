using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Repositories
{
    public interface IBookingRepository
    {
        Task AddAsync(Booking booking);
        Task<List<Booking>> GetByUserAsync(Guid userId, string viewAs);
        Task<Booking?> GetByIdWithDetailsForUserAsync(Guid bookingId, Guid userId);
        Task<Booking?> GetByIdForParticipantAsync(Guid bookingId, Guid userId);
        Task<Booking?> GetByIdForCustomerAsync(Guid bookingId, Guid customerId);
        Task<List<Booking>> GetBookingsByDateAsync(Guid muaId, DateTime date);
    }
}
