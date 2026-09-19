namespace BeautyBookBackend.Models.Enums
{
    public enum PayoutStatus : byte
    {
        Pending = 0,
        ManualActionRequired = 1,
        Processing = 2,
        Paid = 3,
        Failed = 4
    }
}
