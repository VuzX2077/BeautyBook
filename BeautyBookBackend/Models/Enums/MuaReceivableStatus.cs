namespace BeautyBookBackend.Models.Enums
{
    public enum MuaReceivableStatus : byte
    {
        OnHold = 0,
        Available = 1,
        Frozen = 2,
        PayoutPending = 3,
        PaidOut = 4,
        Reversed = 5
    }
}
