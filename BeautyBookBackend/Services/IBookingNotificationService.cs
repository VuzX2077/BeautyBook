using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services;

public interface IBookingNotificationService
{
    Task RegisterDeviceAsync(Guid userId, string token, string platform, string? deviceName);
    Task UnregisterDeviceAsync(Guid userId, string token);
    Task QueueBookingStatusAsync(Booking booking, BookingStatus newStatus, Guid actorId);
    Task ScheduleRemindersAsync(Booking booking);
    Task CancelPendingAsync(Guid bookingId);
}
