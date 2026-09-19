using BeautyBookBackend.Services;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public class BookingTimeServiceTests
{
    private readonly BookingTimeService _bookingTime = new(ResolveVietnamTimeZone());

    [Fact]
    public void VietnamAppointmentAtNineteen_IsTwelveUtc()
    {
        var result = _bookingTime.ToUtc(new DateTime(2026, 9, 20), TimeSpan.FromHours(19));

        Assert.Equal(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void ConversionDoesNotDependOnInputKindOrMachineLocalTimezone(DateTimeKind kind)
    {
        var date = DateTime.SpecifyKind(new DateTime(2026, 9, 20), kind);

        var result = _bookingTime.ToUtc(date, TimeSpan.FromHours(19));

        Assert.Equal(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GraceDecisionForVietnamAppointment_IsIndependentOfHostTimezone()
    {
        var policy = new BookingRefundPolicyService(
            _bookingTime,
            NullLogger<BookingRefundPolicyService>.Instance);
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            BookingDate = new DateTime(2026, 9, 20),
            StartTime = TimeSpan.FromHours(19),
            DepositAmount = 120_000m,
            Status = BookingStatus.Approved,
            PaymentStatus = PaymentStatus.DepositHeld,
            ConfirmedAt = new DateTime(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc)
        };

        var decision = policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            new DateTime(2026, 9, 20, 11, 20, 0, DateTimeKind.Utc),
            booking.DepositAmount);

        Assert.Equal(BookingRefundPolicyService.FullWithinGracePeriod, decision.PolicyRule);
        Assert.Equal(100m, decision.RefundPercentage);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), decision.AppointmentAtUtc);
    }

    [Fact]
    public void UnspecifiedTimeOffInput_IsInterpretedAsVietnamBusinessTime()
    {
        var input = new DateTime(2026, 9, 20, 19, 0, 0, DateTimeKind.Unspecified);

        var result = _bookingTime.NormalizeInstantToUtc(input);

        Assert.Equal(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), result);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
