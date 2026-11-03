using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed record NativeCommerceOrder(Guid Id, string ProductId, int AmountVnd, string Currency, string State,
    DateTime CreatedAt, DateTime ExpiresAt, DateTime? VerifiedPaidAt, NativeCommercePayment? Payment);

// Only call Capture with provider data whose signature, amount, order and link
// have already been verified. Never generate bank instructions from app input.
public sealed record NativeCommercePayment(string QrCode, string BankBin, string AccountNumber, string AccountName, string Description)
{
    public static string? Capture(JsonElement verifiedData)
    {
        string Read(string key) => verifiedData.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var payment = new NativeCommercePayment(Read("qrCode"), Read("bin"), Read("accountNumber"), Read("accountName"), Read("description"));
        if (string.IsNullOrWhiteSpace(payment.QrCode)) return null; // Legacy recovered links may not contain QR data.
        if (string.IsNullOrWhiteSpace(payment.BankBin) || string.IsNullOrWhiteSpace(payment.AccountNumber) || string.IsNullOrWhiteSpace(payment.AccountName) || string.IsNullOrWhiteSpace(payment.Description))
            throw new CommerceException("INVALID_PAYMENT_DETAILS");
        if (payment.QrCode.Length > 4096 || payment.BankBin.Length > 20 || payment.AccountNumber.Length > 100 || payment.AccountName.Length > 200 || payment.Description.Length > 250)
            throw new CommerceException("INVALID_PAYMENT_DETAILS");
        return JsonSerializer.Serialize(payment);
    }

    public static NativeCommercePayment? Read(string? json) => json == null ? null : JsonSerializer.Deserialize<NativeCommercePayment>(json);
}
