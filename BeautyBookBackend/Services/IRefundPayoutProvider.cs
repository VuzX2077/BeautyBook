namespace BeautyBookBackend.Services
{
    public record RefundPayoutRequest(string ReferenceId, long Amount, string Description, string BankBin, string AccountNumber);
    public record RefundPayoutResult(string PayoutId, string State, string? TransactionReference, bool Completed, bool Failed, string? FailureCode, string? FailureMessage);

    public interface IRefundPayoutProvider
    {
        Task<RefundPayoutResult> CreateAsync(RefundPayoutRequest request, string idempotencyKey);
        Task<RefundPayoutResult> GetAsync(string payoutId);
    }
}
