using System.Security.Claims;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using SkiaSharp;

namespace BeautyBookBackend.Tests;

public sealed class FinancialQrSecurityTests
{
    private const string BankPayload = "00020101021138540010A00000072701240006970436011001234567890208QRIBFTTA53037045802VN5909TEST USER63044634";
    private static byte[] Qr(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(8);
    }
    private static IFormFile File(byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "test.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
    private static UploadController Controller(Storage storage) => new(storage) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test")) } } };

    [Theory]
    [InlineData(BankPayload)]
    [InlineData("https://nhantien.momo.vn/0912345678")]
    public async Task DecodeOnlyNeverCallsStorageOrReturnsQrPayloadOrUrl(string payload)
    {
        var storage = new Storage(); var controller = Controller(storage);
        var response = Assert.IsType<OkObjectResult>(await controller.UploadBankQr(File(Qr(payload))));
        var json = System.Text.Json.JsonSerializer.Serialize(response.Value);
        Assert.DoesNotContain("Url", json); Assert.DoesNotContain("https://", json);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var result = document.RootElement;
        Assert.Equal(payload == BankPayload ? "BANK" : "MOMO", result.GetProperty("Method").GetString());
        Assert.Equal(payload == BankPayload ? "0123456789" : "0912345678", result.GetProperty("AccountNumber").GetString());
        if (payload != BankPayload) {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, result.GetProperty("AccountName").ValueKind);
            Assert.True(result.GetProperty("RequiresManualAccountName").GetBoolean());
        }
        Assert.DoesNotContain("RawPayload", json); Assert.DoesNotContain(payload, json);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString()); Assert.Equal(0, storage.Calls);
    }
    private static string Tlv(string tag, string value) => tag + value.Length.ToString("D2") + value;
    private static string MultiAppPayload(string bin = "971025", string service = "QRIBFTTA") =>
        Tlv("00", "01") + Tlv("38", Tlv("00", "A000000727") + Tlv("01", Tlv("00", bin) + Tlv("01", "0000000000000000000")) + Tlv("02", service));

    [Fact]
    public async Task MomoMultiAppDecodesWithoutInventingPhoneAndCannotEnterPublicStorage()
    {
        var payload = MultiAppPayload();
        var decoded = BankQrDecoder.DecodeImage(Qr(payload));
        Assert.Equal("MOMO", decoded.Method); Assert.Equal("971025", decoded.BankBin);
        Assert.Null(decoded.AccountNumber); Assert.Null(decoded.AccountName);
        var storage = new Storage();
        var response = Assert.IsType<OkObjectResult>(await Controller(storage).UploadBankQr(File(Qr(payload))));
        var json = System.Text.Json.JsonSerializer.Serialize(response.Value);
        Assert.DoesNotContain("RawPayload", json);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("RequiresManualAccountNumber").GetBoolean());
        Assert.True(document.RootElement.GetProperty("RequiresManualAccountName").GetBoolean());
        foreach (var purpose in new[] { "avatar", "portfolio", "service", "review" })
            Assert.IsType<BadRequestObjectResult>(await Controller(storage).UploadImage(File(Qr(payload)), purpose));
        Assert.Equal(0, storage.Calls);
        Assert.Equal("BANK", BankQrDecoder.ParsePayload(MultiAppPayload("970436")).Method);
        Assert.Equal("BANK", BankQrDecoder.ParsePayload(MultiAppPayload(service: "OTHER")).Method);
    }

    [Fact]
    public async Task InvalidBytesAndUntrustedMimeAreRejectedWithoutStorage()
    {
        var storage = new Storage(); var controller = Controller(storage);
        Assert.IsType<BadRequestObjectResult>(await controller.UploadBankQr(File([1, 2, 3])));
        var spoofed = (FormFile)File(Qr(BankPayload)); spoofed.ContentType = "text/plain";
        Assert.IsType<BadRequestObjectResult>(await controller.UploadBankQr(spoofed));
        Assert.IsType<BadRequestObjectResult>(await controller.UploadBankQr(File(new byte[5 * 1024 * 1024 + 1])));
        using var bitmap = new SKBitmap(16, 16); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var missing = Assert.IsType<BadRequestObjectResult>(await controller.UploadBankQr(File(png.ToArray())));
        Assert.Contains("QR_NOT_RECOGNIZED", System.Text.Json.JsonSerializer.Serialize(missing.Value));
        Assert.Equal(0, storage.Calls);
    }
    [Theory]
    [InlineData("avatar")]
    [InlineData("portfolio")]
    [InlineData("service")]
    [InlineData("review")]
    public async Task FinancialQrCannotBypassProtectionWithPublicPurpose(string purpose)
    {
        var storage = new Storage(); var controller = Controller(storage);
        var result = Assert.IsType<BadRequestObjectResult>(await controller.UploadImage(File(Qr(BankPayload)), purpose));
        Assert.Contains("FINANCIAL_QR_PUBLIC_FORBIDDEN", System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.Equal(0, storage.Calls);
    }
    [Theory]
    [InlineData("avatar")]
    [InlineData("portfolio")]
    [InlineData("service")]
    [InlineData("review")]
    public async Task OrdinaryPublicImagesStillUseOwnedPublicStorage(string purpose)
    {
        using var bitmap = new SKBitmap(16, 16); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var storage = new Storage();
        Assert.IsType<OkObjectResult>(await Controller(storage).UploadImage(File(png.ToArray()), purpose));
        Assert.Equal(1, storage.Calls);
    }
    private sealed class Storage : IImageStorage
    {
        public int Calls;
        public Task<string> UploadPublicImageAsync(Stream content, string contentType, string extension, CancellationToken cancellationToken = default) => throw new Exception("Unowned upload forbidden");
        public Task<string> UploadOwnedPublicImageAsync(Guid owner, Stream content, string contentType, string extension, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult("https://public.test/owned.jpg"); }
    }
}
