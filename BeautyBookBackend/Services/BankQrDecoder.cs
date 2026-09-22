using System.Text.RegularExpressions;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

namespace BeautyBookBackend.Services;

public sealed record BankQrData(string Method, string? BankBin, string? AccountNumber, string? AccountName, string RawPayload);

public static partial class BankQrDecoder
{
    public static BankQrData DecodeImage(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Không đọc được ảnh QR.");
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
        };
        var result = reader.Decode(bitmap) ?? throw new InvalidOperationException("Không tìm thấy mã QR rõ nét trong ảnh.");
        return ParsePayload(result.Text);
    }

    public static BankQrData ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new InvalidOperationException("QR không có dữ liệu.");
        var root = ParseTlv(payload);
        if (root.TryGetValue("38", out var merchantAccount))
        {
            var accountTemplate = ParseTlv(merchantAccount);
            if (accountTemplate.TryGetValue("00", out var guid) && guid == "A000000727"
                && accountTemplate.TryGetValue("01", out var beneficiary))
            {
                var fields = ParseTlv(beneficiary);
                fields.TryGetValue("00", out var bin);
                fields.TryGetValue("01", out var account);
                root.TryGetValue("59", out var name);
                if (string.IsNullOrWhiteSpace(bin) || string.IsNullOrWhiteSpace(account))
                    throw new InvalidOperationException("QR VietQR thiếu ngân hàng hoặc số tài khoản.");
                return new("BANK", bin, account, CleanName(name), payload);
            }
        }

        if (payload.Contains("momo", StringComparison.OrdinalIgnoreCase))
        {
            var phone = PhoneRegex().Match(Uri.UnescapeDataString(payload)).Value;
            return new("MOMO", "MOMO", string.IsNullOrWhiteSpace(phone) ? null : phone, null, payload);
        }

        throw new InvalidOperationException("Chỉ chấp nhận QR chuyển khoản VietQR hoặc QR cá nhân MoMo.");
    }

    private static Dictionary<string, string> ParseTlv(string value)
    {
        var result = new Dictionary<string, string>();
        for (var index = 0; index + 4 <= value.Length;)
        {
            var tag = value.Substring(index, 2);
            if (!int.TryParse(value.AsSpan(index + 2, 2), out var length) || index + 4 + length > value.Length) break;
            result[tag] = value.Substring(index + 4, length);
            index += 4 + length;
        }
        return result;
    }

    private static string? CleanName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    [GeneratedRegex(@"(?<!\d)(?:0|84)\d{8,10}(?!\d)")]
    private static partial Regex PhoneRegex();
}
