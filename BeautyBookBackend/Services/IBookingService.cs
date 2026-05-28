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
        Task<List<BookingDto>> GetBookingsAsync(Guid userId, UserRole role);
        Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId);
        Task<bool> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus status);
        
        // Reviews
        Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto);
        Task<List<ReviewDto>> GetMuaReviewsAsync(Guid muaId);
    }
}
