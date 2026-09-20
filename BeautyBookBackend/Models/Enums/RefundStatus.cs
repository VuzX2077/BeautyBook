namespace BeautyBookBackend.Models.Enums
{
    public enum RefundStatus : byte
    {
        Pending = 0,
        ManualActionRequired = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
        AwaitingDestination = 5
    }
}
