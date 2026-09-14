using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class WalletDto
    {
        public Guid WalletId { get; set; }
        public Guid UserId { get; set; }
        public decimal Balance { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<TransactionDto> Transactions { get; set; } = new();
    }

    public class TransactionDto
    {
        public Guid TransactionId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Amount { get; set; }
        public TransactionType TransactionType { get; set; }
        public Guid? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DepositDto
    {
        [Required]
        [Range(10000, 100000000, ErrorMessage = "Deposit amount must be at least 10,000 VND.")]
        public decimal Amount { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }
    }

    public class CreateTopUpDto
    {
        [Required]
        [Range(10000, 100000000, ErrorMessage = "Top-up amount must be between 10,000 and 100,000,000 VND.")]
        public decimal Amount { get; set; }

        [Url]
        public string? ReturnUrl { get; set; }

        [Url]
        public string? CancelUrl { get; set; }
    }

    public class WalletTopUpDto
    {
        public Guid TopUpId { get; set; }
        public Guid UserId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Amount { get; set; }
        public PaymentProvider Provider { get; set; }
        public long ProviderOrderCode { get; set; }
        public string? ProviderPaymentLinkId { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public TopUpStatus Status { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PayOsWebhookDto
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public PayOsWebhookDataDto? Data { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    public class PayOsWebhookDataDto
    {
        [JsonPropertyName("orderCode")]
        public long OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("transactionDateTime")]
        public string? TransactionDateTime { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paymentLinkId")]
        public string? PaymentLinkId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }
}
