namespace BeautyBookBackend.Models.Enums
{
    public enum TopUpStatus : byte
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2,
        Failed = 3,
        Expired = 4
    }
}
