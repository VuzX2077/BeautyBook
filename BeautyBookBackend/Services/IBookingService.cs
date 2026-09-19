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
        Task<BookingPaymentDto?> CreateDepositPaymentAsync(Guid bookingId, Guid customerId);
        Task<bool> HandlePayOsWebhookAsync(PayOsWebhookDto webhook);
        Task<List<BookingDto>> GetBookingsAsync(Guid userId, string viewAs);
        Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId);
        Task<BookingDto?> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus status, string? reason = null);
        Task<BookingDto?> ResolveDisputeAsync(Guid bookingId, bool refundCustomer);
        Task<int> AutoCompleteOverdueAsync();
        Task<int> ExpirePendingPaymentsAsync();
        Task<List<TimeSpan>> GetAvailableSlotsAsync(Guid muaId, DateTime date, int totalDurationMinutes);
        
        // Reviews
        Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto);
        Task<List<ReviewDto>> GetMuaReviewsAsync(Guid muaId);
        Task<bool> ReplyReviewAsync(Guid reviewId, Guid muaId, string replyContent, bool isAdmin = false);
    }
}
