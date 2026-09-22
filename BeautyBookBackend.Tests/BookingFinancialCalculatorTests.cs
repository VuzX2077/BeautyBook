using BeautyBookBackend.Services;

namespace BeautyBookBackend.Tests;

public sealed class BookingFinancialCalculatorTests
{
    [Fact]
    public void Fee_IsEightPercentOfTotal_NotDeposit()
    {
        var result = BookingFinancialCalculator.Calculate(1_000_000m);
        Assert.Equal(300_000m, result.DepositAmount);
        Assert.Equal(80_000m, result.PlatformFeeAmount);
        Assert.Equal(220_000m, result.MuaDepositPayoutAmount);
        Assert.Equal(700_000m, result.RemainingAmount);
    }

    [Fact]
    public void RejectsNonPositiveTotal() => Assert.Throws<ArgumentOutOfRangeException>(() => BookingFinancialCalculator.Calculate(0));
}
