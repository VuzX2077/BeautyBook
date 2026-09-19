namespace BeautyBookBackend.Models.Enums
{
    public enum RefundReasonCode : byte
    {
        MuaRejected = 0,
        MuaCancelled = 1,
        LatePayment = 2,
        MuaConfirmationTimeout = 3,
        DisputeResolvedForCustomer = 4,
        LegacyReconciliation = 5,
        CustomerCancelled = 6
    }
}
