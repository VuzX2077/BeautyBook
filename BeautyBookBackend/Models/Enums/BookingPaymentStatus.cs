namespace BeautyBookBackend.Models.Enums
{
    public enum BookingPaymentStatus : byte
    {
        Created = 0,
        Pending = 1,
        Paid = 2,
        Failed = 3,
        Expired = 4,
        RefundPending = 5,
        Refunded = 6
    }
}
