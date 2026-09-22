namespace BeautyBookBackend.Services;

public sealed record BookingFinancials(decimal DepositAmount, decimal PlatformFeeAmount, decimal MuaDepositPayoutAmount, decimal RemainingAmount);

public static class BookingFinancialCalculator
{
    public const decimal DepositRate = 0.30m;
    public const decimal PlatformFeeRate = 0.08m;
    public const string CurrentPolicyVersion = "V2_TOTAL_FEE";

    public static BookingFinancials Calculate(decimal totalAmount)
    {
        if (totalAmount <= 0) throw new ArgumentOutOfRangeException(nameof(totalAmount));
        var deposit = decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero);
        var fee = decimal.Round(totalAmount * PlatformFeeRate, 0, MidpointRounding.AwayFromZero);
        var payout = deposit - fee;
        if (payout < 0) throw new InvalidOperationException("Phí nền tảng không được vượt quá tiền cọc.");
        return new(deposit, fee, payout, totalAmount - deposit);
    }
}
