using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeautyBookBackend.Services;

// payOS payment-requests signatures use sorted keys without URL encoding.
public static class PayOsSignature
{
    public static bool Verify(JsonElement data, string? signature, string key)
    {
        if (data.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(signature)) return false;
        try
        {
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(Canonicalize(data)));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }

    public static string Canonicalize(JsonElement data) => string.Join("&", data.EnumerateObject()
        .OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => $"{x.Name}={Value(x.Value)}"));

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.String => value.GetString() is null or "null" or "undefined" ? "" : value.GetString()!,
        JsonValueKind.Array or JsonValueKind.Object => SortedJson(value),
        _ => value.GetRawText()
    };

    private static string SortedJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteSorted(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var item in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(item.Name); WriteSorted(writer, item.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteSorted(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
