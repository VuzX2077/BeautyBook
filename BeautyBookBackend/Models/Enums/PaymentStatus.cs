namespace BeautyBookBackend.Models.Enums
{
    public enum PaymentStatus : byte
    {
        Unpaid = 0,
        Paid = 1,
        Refunded = 2,
        Failed = 3,
        DepositHeld = 4,
        Released = 5,
        Frozen = 6,
        RefundPending = 7
    }
}
