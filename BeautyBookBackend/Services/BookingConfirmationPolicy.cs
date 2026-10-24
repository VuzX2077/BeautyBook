using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services;

public static class BookingConfirmationPolicy
{
    public const string TimeoutRule = "MUA_CONFIRMATION_TIMEOUT_FULL";
    public const string TimeoutReason = "MUA không xác nhận trước giờ bắt đầu lịch hẹn.";

    public static bool IsAwaitingMua(Booking booking) =>
        booking.Status is BookingStatus.PendingConfirmation or BookingStatus.Pending;

    // There is no minimum notice or 24-hour response cap. The appointment itself
    // is the deadline, including for bookings paid only minutes before it starts.
    public static bool IsExpired(Booking booking, BookingTimeService time, DateTime now) =>
        IsAwaitingMua(booking) && time.ToUtc(booking.BookingDate, booking.StartTime) <= now;
}
