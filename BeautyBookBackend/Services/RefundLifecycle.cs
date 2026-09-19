using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public static class RefundLifecycle
    {
        public static void MarkFailed(Refund refund, DateTime failedAtUtc, string? failureCode, string? failureMessage)
        {
            refund.Status = RefundStatus.Failed;
            refund.FailedAt = failedAtUtc;
            refund.UpdatedAt = failedAtUtc;
            refund.FailureCode = failureCode;
            refund.FailureMessage = failureMessage;
        }
    }
}
