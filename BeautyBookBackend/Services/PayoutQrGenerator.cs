using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using SkiaSharp;
using ZXing;

namespace BeautyBookBackend.Services;

/// <summary>Local NAPAS account-transfer QR generation; no network or persistence.</summary>
public static class PayoutQrGenerator
{
    public static string Payload(AdminPayoutDto payout)
    {
        if (payout.Status is not (PayoutStatus.Pending or PayoutStatus.ManualActionRequired or PayoutStatus.Processing))
            throw new InvalidOperationException("Payout không còn ở trạng thái có thể chuyển tiền.");
        if (payout.BankCode == "MOMO")
            throw new InvalidOperationException("Chưa có contract tạo QR nhận tiền MoMo được xác minh. Hãy chuyển theo thông tin MoMo và đối chiếu người nhận.");
        if (!Regex.IsMatch(payout.BankBin ?? "", "^[0-9]{6}$") || !Regex.IsMatch(payout.AccountNumber, "^[A-Z0-9]{5,19}$"))
            throw new InvalidOperationException("Payout thiếu BIN ngân hàng hợp lệ hoặc số tài khoản không phù hợp định dạng VietQR (5–19 ký tự).");
        if (!BankAccountService.BankBinMatchesKnownCode(payout.BankCode, payout.BankBin))
            throw new InvalidOperationException("Mã ngân hàng và BIN trong payout không khớp. Cần đối soát dữ liệu trước khi tạo QR.");
        if (payout.Id == Guid.Empty || payout.Amount <= 0 || payout.Amount != decimal.Truncate(payout.Amount))
            throw new InvalidOperationException("Payout thiếu mã hoặc số tiền VND nguyên hợp lệ.");
        var beneficiary = Tlv("00", payout.BankBin!) + Tlv("01", payout.AccountNumber);
        var merchant = Tlv("00", "A000000727") + Tlv("01", beneficiary) + Tlv("02", "QRIBFTTA");
        // A 25-character payout reference, not a fabricated provider transfer reference.
        var reference = "BB" + payout.Id.ToString("N")[..23].ToUpperInvariant();
        var data = Tlv("00", "01") + Tlv("01", "12") + Tlv("38", merchant)
            + Tlv("53", "704") + Tlv("54", payout.Amount.ToString("0", CultureInfo.InvariantCulture))
            + Tlv("58", "VN") + Tlv("62", Tlv("08", reference)) + "6304";
        return data + Crc(data).ToString("X4", CultureInfo.InvariantCulture);
    }

    public static string ImageDataUrl(AdminPayoutDto payout)
    {
        var matrix = new ZXing.QrCode.QRCodeWriter().encode(Payload(payout), BarcodeFormat.QR_CODE, 512, 512,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++) bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }

    private static string Tlv(string tag, string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        if (length > 99) throw new InvalidOperationException("Thông tin payout vượt quá độ dài VietQR.");
        return tag + length.ToString("D2", CultureInfo.InvariantCulture) + value;
    }
    private static ushort Crc(string data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in Encoding.UTF8.GetBytes(data)) {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
