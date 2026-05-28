namespace BeautyBookBackend.Models.Enums
{
    public enum TransactionType : byte
    {
        Deposit = 0,
        Withdraw = 1,
        BookingPayment = 2,
        BookingEarning = 3,
        Commission = 4
    }
}
