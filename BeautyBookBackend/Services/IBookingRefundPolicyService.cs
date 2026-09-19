using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public record BookingRefundDecision(
        decimal RefundPercentage,
        decimal RefundAmount,
        RefundReasonCode ReasonCode,
        string PolicyRule,
        DateTime AppointmentAtUtc);

    public interface IBookingRefundPolicyService
    {
        BookingRefundDecision Calculate(
            Booking booking,
            BookingStatus requestedStatus,
            BookingCancellationActor actor,
            DateTime cancelledAtUtc,
            decimal capturedAmount);
    }
}
