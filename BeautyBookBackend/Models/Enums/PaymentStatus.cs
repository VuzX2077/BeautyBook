namespace BeautyBookBackend.Models.Enums
{
    public enum PaymentStatus : byte
    {
        Unpaid = 0,
        Paid = 1,
        Refunded = 2,
        Failed = 3
    }
}
