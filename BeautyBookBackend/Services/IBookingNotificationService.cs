using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public interface IBookingNotificationService
{
    Task RegisterDeviceAsync(Guid userId, string token, string platform, string? deviceName);
    Task UnregisterDeviceAsync(Guid userId, string token);
    Task QueueBookingStatusAsync(Booking booking, BookingStatus newStatus, Guid actorId);
    Task ScheduleRemindersAsync(Booking booking);
    Task CancelPendingAsync(Guid bookingId);
    Task<IReadOnlyList<AppNotificationDto>> GetInboxAsync(Guid userId, int take);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId);
    Task MarkAllReadAsync(Guid userId);
}
