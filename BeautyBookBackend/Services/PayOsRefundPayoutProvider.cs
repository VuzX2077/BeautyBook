using PayOS;
using PayOS.Models.V1.Payouts;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public class PayOsRefundPayoutProvider : IRefundPayoutProvider
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext? _db;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        public PayOsRefundPayoutProvider(IConfiguration configuration, ApplicationDbContext? db = null, IHttpContextAccessor? httpContextAccessor = null)
        { _configuration = configuration; _db = db; _httpContextAccessor = httpContextAccessor; }

        public async Task<RefundPayoutResult> CreateAsync(RefundPayoutRequest request, string idempotencyKey)
        {
            if (_db == null) throw new InvalidOperationException("Cannot resolve persisted refund.");
            var refund = await _db.Refunds.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderReferenceId == request.ReferenceId);
            if (refund == null || refund.Amount != request.Amount || refund.DestinationBankBin != request.BankBin
                || refund.DestinationAccountNumber != request.AccountNumber)
                throw new InvalidOperationException("Cannot resolve matching persisted refund.");
            var policy = new PlayReviewPolicy(_db);
            await policy.EnsureNormalRefundAsync(refund.RefundId);
            await policy.EnsureExternalActorAsync(_httpContextAccessor);
            var payout = await Client().Payouts.CreateAsync(new PayoutRequest
            {
                ReferenceId = request.ReferenceId,
                Amount = request.Amount,
                Description = request.Description,
                ToBin = request.BankBin,
                ToAccountNumber = request.AccountNumber,
                Category = new List<string> { "refund" }
            }, idempotencyKey);
            return Map(payout);
        }

        public async Task<RefundPayoutResult> GetAsync(string payoutId)
        {
            if (_db == null) throw new InvalidOperationException("Cannot resolve persisted refund.");
            var refund = await _db.Refunds.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderPayoutId == payoutId)
                ?? throw new InvalidOperationException("Cannot resolve persisted refund.");
            var policy = new PlayReviewPolicy(_db);
            await policy.EnsureNormalRefundAsync(refund.RefundId);
            await policy.EnsureExternalActorAsync(_httpContextAccessor);
            return Map(await Client().Payouts.GetAsync(payoutId));
        }

        private PayOSClient Client() => new(
            Required("PayOS:ClientId"), Required("PayOS:ApiKey"), Required("PayOS:ChecksumKey"),
            _configuration["PayOS:PartnerCode"] ?? string.Empty);

        private string Required(string key) => _configuration[key]
            ?? throw new InvalidOperationException($"{key} chưa được cấu hình.");

        private static RefundPayoutResult Map(Payout payout)
        {
            var transaction = payout.Transactions?.OrderByDescending(x => x.TransactionDatetime).FirstOrDefault();
            var state = payout.ApprovalState.ToString();
            var completed = payout.ApprovalState == PayoutApprovalState.Completed
                || (payout.Transactions?.Count > 0 && payout.Transactions.All(x => x.State == PayoutTransactionState.Succeeded));
            var failed = payout.ApprovalState is PayoutApprovalState.Failed or PayoutApprovalState.Rejected or PayoutApprovalState.Cancelled
                || (payout.Transactions?.Count > 0 && payout.Transactions.All(x => x.State is PayoutTransactionState.Failed or PayoutTransactionState.Cancelled or PayoutTransactionState.Reversed));
            return new RefundPayoutResult(payout.Id, state, transaction?.Reference, completed, failed, transaction?.ErrorCode, transaction?.ErrorMessage);
        }
    }
}
