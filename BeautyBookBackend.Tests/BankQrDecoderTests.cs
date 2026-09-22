using BeautyBookBackend.Services;
using QRCoder;

namespace BeautyBookBackend.Tests;

public class BankQrDecoderTests
{
    [Fact]
    public void ParsePayload_ExtractsVietQrBeneficiary()
    {
        const string payload = "00020101021138540010A00000072701240006970436011001234567890208QRIBFTTA53037045802VN5912NGUYEN VAN A6304ABCD";

        var result = BankQrDecoder.ParsePayload(payload);

        Assert.Equal("BANK", result.Method);
        Assert.Equal("970436", result.BankBin);
        Assert.Equal("0123456789", result.AccountNumber);
        Assert.Equal("NGUYEN VAN A", result.AccountName);
    }

    [Fact]
    public void ParsePayload_RejectsUnrelatedQr()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BankQrDecoder.ParsePayload("https://example.com"));
        Assert.Contains("VietQR", error.Message);
    }

    [Fact]
    public void ParsePayload_RecognizesMomoAndPhoneWhenPresent()
    {
        var result = BankQrDecoder.ParsePayload("https://nhantien.momo.vn/0912345678");
        Assert.Equal("MOMO", result.Method);
        Assert.Equal("0912345678", result.AccountNumber);
    }

    [Fact]
    public void DecodeImage_ReadsUploadedPngQr()
    {
        const string payload = "00020101021138540010A00000072701240006970436011001234567890208QRIBFTTA53037045802VN5912NGUYEN VAN A6304ABCD";
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);

        var result = BankQrDecoder.DecodeImage(png);

        Assert.Equal("970436", result.BankBin);
        Assert.Equal("0123456789", result.AccountNumber);
    }
}
