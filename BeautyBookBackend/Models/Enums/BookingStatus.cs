namespace BeautyBookBackend.Models.Enums
{
    public enum BookingStatus : byte
    {
        Pending = 0,
        Approved = 1,
        Completed = 2,
        Cancelled = 3,
        WaitingCustomer = 4,
        PendingPayment = 5,
        PendingConfirmation = 6,
        Rejected = 7,
        InProgress = 8,
        Disputed = 9,
        AutoCompleted = 10
    }
}
