using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class BookingRefundPolicyService : IBookingRefundPolicyService
    {
        private readonly BookingTimeService _bookingTime;
        private readonly ILogger<BookingRefundPolicyService> _logger;

        public const string FullBeforeMuaAccepts = "CUSTOMER_BEFORE_MUA_ACCEPTED_FULL";
        public const string FullAtLeast24Hours = "CUSTOMER_AT_LEAST_24_HOURS_FULL";
        public const string FullWithinGracePeriod = "CUSTOMER_GRACE_PERIOD_FULL";
        public const string HalfAtLeast6Hours = "CUSTOMER_AT_LEAST_6_HOURS_HALF";
        public const string NoneUnder6Hours = "CUSTOMER_UNDER_6_HOURS_NONE";
        public const string MuaRejectedFull = "MUA_REJECTED_FULL";
        public const string MuaCancelledFull = "MUA_CANCELLED_FULL";

        public BookingRefundPolicyService(
            BookingTimeService bookingTime,
            ILogger<BookingRefundPolicyService> logger)
        {
            _bookingTime = bookingTime;
            _logger = logger;
        }

        public BookingRefundDecision Calculate(
            Booking booking,
            BookingStatus requestedStatus,
            BookingCancellationActor actor,
            DateTime cancelledAtUtc,
            decimal capturedAmount)
        {
            if (capturedAmount < 0)
                throw new BookingRuleException("INVALID_CAPTURED_AMOUNT", "Số tiền đã thanh toán không hợp lệ.");

            cancelledAtUtc = cancelledAtUtc.Kind == DateTimeKind.Utc
                ? cancelledAtUtc
                : DateTime.SpecifyKind(cancelledAtUtc, DateTimeKind.Utc);
            var appointmentAtUtc = _bookingTime.ToUtc(booking.BookingDate, booking.StartTime);

            if (booking.Status == BookingStatus.InProgress || booking.StartedAt.HasValue || appointmentAtUtc <= cancelledAtUtc)
                throw new BookingRuleException(
                    "BOOKING_ALREADY_STARTED",
                    "Booking đã đến giờ thực hiện. Vui lòng sử dụng quy trình khiếu nại nếu có vấn đề.",
                    409);

            decimal percentage;
            RefundReasonCode reasonCode;
            string rule;

            if (actor == BookingCancellationActor.Mua)
            {
                percentage = 100m;
                var rejected = requestedStatus == BookingStatus.Rejected;
                reasonCode = rejected ? RefundReasonCode.MuaRejected : RefundReasonCode.MuaCancelled;
                rule = rejected ? MuaRejectedFull : MuaCancelledFull;
            }
            else if (actor == BookingCancellationActor.Customer)
            {
                reasonCode = RefundReasonCode.CustomerCancelled;
                if (booking.Status is BookingStatus.PendingPayment or BookingStatus.PendingConfirmation or BookingStatus.Pending)
                {
                    percentage = 100m;
                    rule = FullBeforeMuaAccepts;
                }
                else if (booking.Status == BookingStatus.Approved)
                {
                    if (booking.ConfirmedAt.HasValue)
                    {
                        var confirmedAtUtc = DateTime.SpecifyKind(booking.ConfirmedAt.Value, DateTimeKind.Utc);
                        var gracePeriodEnd = confirmedAtUtc.AddMinutes(30) < appointmentAtUtc
                            ? confirmedAtUtc.AddMinutes(30)
                            : appointmentAtUtc;
                        if (cancelledAtUtc <= gracePeriodEnd && cancelledAtUtc < appointmentAtUtc)
                        {
                            percentage = 100m;
                            rule = FullWithinGracePeriod;
                            var graceAmount = decimal.Round(booking.DepositAmount, 0, MidpointRounding.AwayFromZero);
                            return new BookingRefundDecision(
                                percentage,
                                Math.Min(graceAmount, capturedAmount),
                                reasonCode,
                                rule,
                                appointmentAtUtc);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Approved booking {BookingId} has no ConfirmedAt; using the standard 24h/6h cancellation policy.",
                            booking.BookingId);
                    }

                    var remaining = appointmentAtUtc - cancelledAtUtc;
                    if (remaining >= TimeSpan.FromHours(24))
                    {
                        percentage = 100m;
                        rule = FullAtLeast24Hours;
                    }
                    else if (remaining >= TimeSpan.FromHours(6))
                    {
                        percentage = 50m;
                        rule = HalfAtLeast6Hours;
                    }
                    else
                    {
                        percentage = 0m;
                        rule = NoneUnder6Hours;
                    }
                }
                else
                {
                    throw new BookingRuleException("INVALID_CANCELLATION_STATE", "Booking không ở trạng thái customer có thể hủy.", 409);
                }
            }
            else
            {
                throw new BookingRuleException("INVALID_CANCELLATION_ACTOR", "Chủ thể hủy booking không hợp lệ.");
            }

            var policyAmount = decimal.Round(booking.DepositAmount * percentage / 100m, 0, MidpointRounding.AwayFromZero);
            var refundAmount = Math.Min(policyAmount, capturedAmount);
            return new BookingRefundDecision(percentage, refundAmount, reasonCode, rule, appointmentAtUtc);
        }

    }
}
