using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services.Monetization;

// Pure protocol adapter. No HTTP transport, credentials from config, webhook route,
// checkout URL exposure or production DI registration is supplied in this phase.
public static class PayOsCommerceProtocol
{
    public static JsonElement CreateLinkRequest(MuaCommerceOrder order, long code, string returnUrl, string cancelUrl, string checksumKey, string trustedReturnHost = "bbookmakeup.com")
    {
        RequireKey(checksumKey);
        if (code <= 0 || order.Currency != "VND" || order.AmountVnd != MuaCommerceCatalog.Get(order.ProductId).AmountVnd)
            throw new CommerceException("INVALID_ORDER");
        foreach (var url in new[] { returnUrl, cancelUrl })
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0
                || !(uri.Scheme == "https" && uri.Host == trustedReturnHost || uri.IsLoopback && uri.Scheme == "http"))
                throw new CommerceException("INVALID_RETURN_URL");
        // Nine characters, independent of bank-specific description length limits.
        var description = order.ProductId == MuaCommerceCatalog.CustomerAi ? "BBAILIFE" : order.ProductId == MuaCommerceCatalog.Activation ? "BBACTIVE" : "BBVIS30D";
        var signed = JsonSerializer.SerializeToElement(new { amount = order.AmountVnd, cancelUrl, description, orderCode = code, returnUrl });
        return JsonSerializer.SerializeToElement(new { amount = order.AmountVnd, cancelUrl, description, orderCode = code, returnUrl,
            expiredAt = checked((int)new DateTimeOffset(order.ExpiresAt).ToUnixTimeSeconds()), signature = Sign(signed, checksumKey) });
    }

    public static JsonElement VerifiedData(JsonElement envelope, string checksumKey)
    {
        RequireKey(checksumKey);
        if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || code.GetString() != "00"
            || !envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.String
            || !PayOsSignature.Verify(data, signature.GetString(), checksumKey))
            throw new CommerceException("INVALID_PAYOS_SIGNATURE_OR_RESULT");
        return data;
    }

    public static string Text(JsonElement data, string key) => data.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new CommerceException("INCOMPLETE_PAYOS_DATA");
    public static long Number(JsonElement data, string key) => data.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result : throw new CommerceException("INCOMPLETE_PAYOS_DATA");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Sign(JsonElement data, string checksumKey)
    {
        RequireKey(checksumKey);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(PayOsSignature.Canonicalize(data)))).ToLowerInvariant();
    }
    private static void RequireKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new CommerceException("MISSING_PAYOS_KEY");
    }
}
