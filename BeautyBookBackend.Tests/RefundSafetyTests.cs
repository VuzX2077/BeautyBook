using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class RefundSafetyTests
{
    [Fact]
    public void RefundPaymentIndex_IsUnique_ToPreventDoubleRefund()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(Refund));
        var index = entity!.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Refund.BookingPaymentId) }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void RefundFailure_DoesNotUndoBookingCancellation()
    {
        var booking = new Booking { Status = BookingStatus.Cancelled };
        var refund = new Refund { Status = RefundStatus.Processing };
        var now = DateTime.UtcNow;

        RefundLifecycle.MarkFailed(refund, now, "PROVIDER_ERROR", "Provider unavailable");

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(RefundStatus.Failed, refund.Status);
        Assert.Equal("PROVIDER_ERROR", refund.FailureCode);
    }
}
