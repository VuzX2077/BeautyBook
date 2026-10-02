using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services;

public static class ComplaintPolicy
{
    public const int WindowHours = 48;
    public static DateTime? Deadline(Booking booking) => booking.CompletedAt?.AddHours(WindowHours);
    public static bool CanCreate(Booking booking, DateTime now) => booking.Status is
        BookingStatus.Approved or BookingStatus.InProgress or BookingStatus.WaitingCustomer or BookingStatus.Disputed
        || (booking.Status is BookingStatus.Completed or BookingStatus.AutoCompleted && Deadline(booking) > now);
    // The support window is independent of payout availability. Completion opens earnings immediately.
    public static bool HasHold(Booking booking, DateTime now) => booking.CompletedAt == null;
}
