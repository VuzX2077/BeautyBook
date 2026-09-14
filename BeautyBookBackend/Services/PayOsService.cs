using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeautyBookBackend.Services
{
    public class PayOsService : IPayOsService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public PayOsService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<PayOsCreatePaymentResult> CreatePaymentLinkAsync(PayOsCreatePaymentRequest request)
        {
            var clientId = GetRequiredConfig("PayOS:ClientId");
            var apiKey = GetRequiredConfig("PayOS:ApiKey");

            var payload = new PayOsCreatePaymentPayload
            {
                OrderCode = request.OrderCode,
                Amount = request.Amount,
                Description = request.Description,
                ReturnUrl = request.ReturnUrl,
                CancelUrl = request.CancelUrl,
                ExpiredAt = request.ExpiredAt,
                Signature = CreatePaymentLinkSignature(request)
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v2/payment-requests")
            {
                Content = JsonContent.Create(payload)
            };
            httpRequest.Headers.Add("x-client-id", clientId);
            httpRequest.Headers.Add("x-api-key", apiKey);

            var response = await _httpClient.SendAsync(httpRequest);
            var payOsResponse = await response.Content.ReadFromJsonAsync<PayOsCreatePaymentResponse>();

            if (!response.IsSuccessStatusCode || payOsResponse?.Code != "00" || payOsResponse.Data == null)
            {
                var message = payOsResponse?.Desc ?? response.ReasonPhrase ?? "PayOS request failed.";
                throw new InvalidOperationException($"Tạo link thanh toán payOS thất bại: {message}");
            }

            return new PayOsCreatePaymentResult
            {
                PaymentLinkId = payOsResponse.Data.PaymentLinkId ?? string.Empty,
                CheckoutUrl = payOsResponse.Data.CheckoutUrl ?? string.Empty,
                QrCode = payOsResponse.Data.QrCode ?? string.Empty,
                Status = payOsResponse.Data.Status ?? string.Empty
            };
        }

        public bool IsValidWebhookSignature(PayOsWebhookVerificationData data)
        {
            if (string.IsNullOrWhiteSpace(data.Signature))
            {
                return false;
            }

            var fields = new SortedDictionary<string, string>
            {
                ["amount"] = data.Amount.ToString(CultureInfo.InvariantCulture),
                ["orderCode"] = data.OrderCode.ToString(CultureInfo.InvariantCulture)
            };

            AddIfPresent(fields, "accountNumber", data.AccountNumber);
            AddIfPresent(fields, "code", data.Code);
            AddIfPresent(fields, "currency", data.Currency);
            AddIfPresent(fields, "desc", data.Desc);
            AddIfPresent(fields, "description", data.Description);
            AddIfPresent(fields, "paymentLinkId", data.PaymentLinkId);
            AddIfPresent(fields, "reference", data.Reference);
            AddIfPresent(fields, "transactionDateTime", data.TransactionDateTime);

            if (data.ExtraData != null)
            {
                foreach (var item in data.ExtraData)
                {
                    if (!fields.ContainsKey(item.Key) && item.Value.ValueKind != JsonValueKind.Null)
                    {
                        fields[item.Key] = item.Value.ValueKind == JsonValueKind.String
                            ? item.Value.GetString() ?? string.Empty
                            : item.Value.ToString();
                    }
                }
            }

            var dataToSign = string.Join("&", fields.Select(x => $"{x.Key}={x.Value}"));
            var expectedSignature = HmacSha256(dataToSign, GetRequiredConfig("PayOS:ChecksumKey"));

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(data.Signature));
        }

        private string CreatePaymentLinkSignature(PayOsCreatePaymentRequest request)
        {
            var dataToSign = string.Join("&", new[]
            {
                $"amount={request.Amount}",
                $"cancelUrl={request.CancelUrl}",
                $"description={request.Description}",
                $"orderCode={request.OrderCode}",
                $"returnUrl={request.ReturnUrl}"
            });

            return HmacSha256(dataToSign, GetRequiredConfig("PayOS:ChecksumKey"));
        }

        private string GetRequiredConfig(string key)
        {
            return _configuration[key]
                ?? throw new InvalidOperationException($"{key} chưa được cấu hình.");
        }

        private static void AddIfPresent(IDictionary<string, string> fields, string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                fields[key] = value;
            }
        }

        private static string HmacSha256(string data, string checksumKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private class PayOsCreatePaymentPayload
        {
            [JsonPropertyName("orderCode")]
            public long OrderCode { get; set; }

            [JsonPropertyName("amount")]
            public int Amount { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("returnUrl")]
            public string ReturnUrl { get; set; } = string.Empty;

            [JsonPropertyName("cancelUrl")]
            public string CancelUrl { get; set; } = string.Empty;

            [JsonPropertyName("expiredAt")]
            public int ExpiredAt { get; set; }

            [JsonPropertyName("signature")]
            public string Signature { get; set; } = string.Empty;
        }

        private class PayOsCreatePaymentResponse
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("desc")]
            public string? Desc { get; set; }

            [JsonPropertyName("data")]
            public PayOsCreatePaymentData? Data { get; set; }
        }

        private class PayOsCreatePaymentData
        {
            [JsonPropertyName("paymentLinkId")]
            public string? PaymentLinkId { get; set; }

            [JsonPropertyName("checkoutUrl")]
            public string? CheckoutUrl { get; set; }

            [JsonPropertyName("qrCode")]
            public string? QrCode { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }
        }
    }
}
