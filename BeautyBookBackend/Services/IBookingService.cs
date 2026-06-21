using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public interface IBookingService
    {
        Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto);
        Task<List<BookingDto>> GetBookingsAsync(Guid userId, string viewAs);
        Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId);
        Task<bool> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus status);
        Task<List<TimeSpan>> GetAvailableSlotsAsync(Guid muaId, DateTime date, int totalDurationMinutes);
        
        // Reviews
        Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto);
        Task<List<ReviewDto>> GetMuaReviewsAsync(Guid muaId);
    }
}
