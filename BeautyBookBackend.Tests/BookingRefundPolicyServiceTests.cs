using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public class BookingRefundPolicyServiceTests
{
    private readonly BookingRefundPolicyService _policy = new(
        new BookingTimeService(TimeZoneInfo.Utc),
        NullLogger<BookingRefundPolicyService>.Instance);
    private static readonly DateTime Now = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CustomerCancelPending_RefundsFullDeposit() =>
        AssertDecision(BookingStatus.PendingConfirmation, BookingStatus.Cancelled, BookingCancellationActor.Customer, 30, 100m, 120_000m);

    [Fact]
    public void MuaReject_RefundsFullDeposit() =>
        AssertDecision(BookingStatus.PendingConfirmation, BookingStatus.Rejected, BookingCancellationActor.Mua, 2, 100m, 120_000m);

    [Fact]
    public void MuaCancelConfirmed_RefundsFullDeposit() =>
        AssertDecision(BookingStatus.Approved, BookingStatus.Cancelled, BookingCancellationActor.Mua, 2, 100m, 120_000m);

    [Theory]
    [InlineData(30, 0, 100, 120_000)]
    [InlineData(24, 0, 100, 120_000)]
    [InlineData(10, 0, 50, 60_000)]
    [InlineData(6, 0, 50, 60_000)]
    [InlineData(5, 59, 0, 0)]
    public void CustomerCancelConfirmed_AppliesTimeBoundaries(
        int hours,
        int minutes,
        decimal expectedPercentage,
        decimal expectedAmount)
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(hours).AddMinutes(minutes));

        var decision = _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            Now,
            booking.DepositAmount);

        Assert.Equal(expectedPercentage, decision.RefundPercentage);
        Assert.Equal(expectedAmount, decision.RefundAmount);
    }

    [Theory]
    [InlineData(BookingStatus.InProgress)]
    [InlineData(BookingStatus.Approved)]
    public void CustomerCancelAfterStart_IsRejected(BookingStatus status)
    {
        var booking = CreateBooking(status, Now);

        var error = Assert.Throws<BookingRuleException>(() => _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            Now,
            booking.DepositAmount));

        Assert.Equal("BOOKING_ALREADY_STARTED", error.Code);
    }

    [Fact]
    public void MuaCancelBeforeStart_AlwaysRefundsFullDeposit() =>
        AssertDecision(BookingStatus.Approved, BookingStatus.Cancelled, BookingCancellationActor.Mua, 1, 100m, 120_000m);

    [Fact]
    public void RefundNeverExceedsCapturedAmount()
    {
        var booking = CreateBooking(BookingStatus.PendingConfirmation, Now.AddHours(30));

        var decision = _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            Now,
            80_000m);

        Assert.Equal(80_000m, decision.RefundAmount);
    }

    [Fact]
    public void DisputedBookingCannotUseCancellationPolicy_AndMoneyStaysFrozen()
    {
        var booking = CreateBooking(BookingStatus.Disputed, Now.AddHours(30));
        booking.PaymentStatus = PaymentStatus.Frozen;

        Assert.Throws<BookingRuleException>(() => _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            Now,
            booking.DepositAmount));
        Assert.Equal(PaymentStatus.Frozen, booking.PaymentStatus);
    }

    [Fact]
    public void CompletedBookingCannotUseCancellationPolicy()
    {
        var booking = CreateBooking(BookingStatus.Completed, Now.AddHours(30));
        Assert.Throws<BookingRuleException>(() => _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            Now,
            booking.DepositAmount));
    }

    [Fact]
    public void CustomerCancelTenMinutesAfterAccept_WithUnderSixHoursRemaining_RefundsFullByGrace()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(5));
        booking.ConfirmedAt = Now.AddMinutes(-10);

        var decision = CancelAsCustomer(booking, Now);

        Assert.Equal(100m, decision.RefundPercentage);
        Assert.Equal(120_000m, decision.RefundAmount);
        Assert.Equal(BookingRefundPolicyService.FullWithinGracePeriod, decision.PolicyRule);
    }

    [Fact]
    public void CustomerCancelExactlyThirtyMinutesAfterAccept_IsStillInGrace()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(5));
        booking.ConfirmedAt = Now.AddMinutes(-30);

        Assert.Equal(BookingRefundPolicyService.FullWithinGracePeriod, CancelAsCustomer(booking, Now).PolicyRule);
    }

    [Fact]
    public void CustomerCancelThirtyMinutesAndOneMillisecondAfterAccept_UsesNormalPolicy()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(5));
        booking.ConfirmedAt = Now.AddMinutes(-30).AddMilliseconds(-1);

        var decision = CancelAsCustomer(booking, Now);

        Assert.Equal(0m, decision.RefundPercentage);
        Assert.Equal(BookingRefundPolicyService.NoneUnder6Hours, decision.PolicyRule);
    }

    [Fact]
    public void ActiveGraceWithAppointmentOverTwentyFourHours_TracksGraceRule()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(30));
        booking.ConfirmedAt = Now.AddMinutes(-20);

        Assert.Equal(BookingRefundPolicyService.FullWithinGracePeriod, CancelAsCustomer(booking, Now).PolicyRule);
    }

    [Fact]
    public void ExpiredGraceWithAppointmentOverTwentyFourHours_UsesNormalFullPolicy()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(30));
        booking.ConfirmedAt = Now.AddHours(-1);

        Assert.Equal(BookingRefundPolicyService.FullAtLeast24Hours, CancelAsCustomer(booking, Now).PolicyRule);
    }

    [Fact]
    public void ExpiredGraceWithTenHoursRemaining_RefundsHalf()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(10));
        booking.ConfirmedAt = Now.AddHours(-1);

        Assert.Equal(50m, CancelAsCustomer(booking, Now).RefundPercentage);
    }

    [Fact]
    public void AppointmentBoundaryWinsEvenWhenGraceWouldStillBeActive()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now);
        booking.ConfirmedAt = Now.AddMinutes(-15);

        var error = Assert.Throws<BookingRuleException>(() => CancelAsCustomer(booking, Now));

        Assert.Equal("BOOKING_ALREADY_STARTED", error.Code);
    }

    [Fact]
    public void LegacyApprovedBookingWithoutConfirmedAt_FallsBackToNormalPolicy()
    {
        var booking = CreateBooking(BookingStatus.Approved, Now.AddHours(5));
        booking.ConfirmedAt = null;

        var decision = CancelAsCustomer(booking, Now);

        Assert.Equal(0m, decision.RefundPercentage);
        Assert.Equal(BookingRefundPolicyService.NoneUnder6Hours, decision.PolicyRule);
    }

    private BookingRefundDecision CancelAsCustomer(Booking booking, DateTime cancelledAtUtc) =>
        _policy.Calculate(
            booking,
            BookingStatus.Cancelled,
            BookingCancellationActor.Customer,
            cancelledAtUtc,
            booking.DepositAmount);

    private void AssertDecision(
        BookingStatus currentStatus,
        BookingStatus requestedStatus,
        BookingCancellationActor actor,
        int hoursUntilAppointment,
        decimal expectedPercentage,
        decimal expectedAmount)
    {
        var booking = CreateBooking(currentStatus, Now.AddHours(hoursUntilAppointment));
        var decision = _policy.Calculate(booking, requestedStatus, actor, Now, booking.DepositAmount);
        Assert.Equal(expectedPercentage, decision.RefundPercentage);
        Assert.Equal(expectedAmount, decision.RefundAmount);
    }

    private static Booking CreateBooking(BookingStatus status, DateTime appointmentAtUtc) => new()
    {
        BookingId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        MUAId = Guid.NewGuid(),
        BookingDate = appointmentAtUtc.Date,
        StartTime = appointmentAtUtc.TimeOfDay,
        DepositAmount = 120_000m,
        Status = status,
        PaymentStatus = PaymentStatus.DepositHeld
    };
}
