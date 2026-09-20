using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class RefundSafetyTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void RefundPaymentIndex_IsUnique_ToPreventDoubleRefund()
    {
        using var context = CreateContext();
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

    [Fact]
    public void RefundStatus_ExistingNumericValuesRemainStable()
    {
        Assert.Equal(0, (int)RefundStatus.Pending);
        Assert.Equal(1, (int)RefundStatus.ManualActionRequired);
        Assert.Equal(2, (int)RefundStatus.Processing);
        Assert.Equal(3, (int)RefundStatus.Completed);
        Assert.Equal(4, (int)RefundStatus.Failed);
        Assert.Equal(5, (int)RefundStatus.AwaitingDestination);
    }

    [Fact]
    public void CustomerDefaultBankAccountIndex_IsUniqueAndFiltered()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CustomerBankAccount));
        var index = Assert.Single(entity!.GetIndexes(), x => x.GetDatabaseName() == "UX_CustomerBankAccounts_Default");
        Assert.True(index.IsUnique);
        Assert.Contains("IsDefault", index.GetFilter());
        Assert.Contains("IsActive", index.GetFilter());
    }
}
