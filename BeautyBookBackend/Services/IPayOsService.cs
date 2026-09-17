namespace BeautyBookBackend.Services
{
    using System.Text.Json;

    public interface IPayOsService
    {
        Task<PayOsCreatePaymentResult> CreatePaymentLinkAsync(PayOsCreatePaymentRequest request);
        bool IsValidWebhookSignature(PayOsWebhookVerificationData data);
    }

    public class PayOsCreatePaymentRequest
    {
        public long OrderCode { get; set; }
        public int Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public int ExpiredAt { get; set; }
    }

    public class PayOsCreatePaymentResult
    {
        public string PaymentLinkId { get; set; } = string.Empty;
        public string CheckoutUrl { get; set; } = string.Empty;
        public string QrCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class PayOsWebhookVerificationData
    {
        public long OrderCode { get; set; }
        public int Amount { get; set; }
        public string? Description { get; set; }
        public string? AccountNumber { get; set; }
        public string? Reference { get; set; }
        public string? TransactionDateTime { get; set; }
        public string? Currency { get; set; }
        public string? PaymentLinkId { get; set; }
        public string? Code { get; set; }
        public string? Desc { get; set; }
        public string? Signature { get; set; }
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }
}
