using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Tests;

public sealed class PayoutQrTests
{
    private static AdminPayoutDto Payout() => new() { Id = Guid.NewGuid(), Status = PayoutStatus.Processing, BankCode = "VCB", BankBin = "970436", AccountNumber = "1234567890", AccountHolderName = "TEST USER", Amount = 12345 };
    [Fact]
    public void OnDemandQrContainsOnlyCurrentPayoutBeneficiaryAmountAndReference()
    {
        var first = Payout(); var second = Payout(); second.Amount = 67890; second.AccountNumber = "9876543210";
        var one = PayoutQrGenerator.Payload(first); var two = PayoutQrGenerator.Payload(second);
        Assert.Equal(first.AccountNumber, BankQrDecoder.ParsePayload(one).AccountNumber);
        Assert.Contains("540512345", one); Assert.DoesNotContain("9876543210", one); Assert.NotEqual(one, two);
        Assert.Contains("540567890", two); Assert.Contains("BB" + second.Id.ToString("N")[..23].ToUpperInvariant(), two);
        Assert.DoesNotContain("http", one); Assert.DoesNotContain("public", one);
        var decoded = BankQrDecoder.DecodeImage(Convert.FromBase64String(PayoutQrGenerator.ImageDataUrl(first).Split(',')[1]));
        Assert.Equal(one, decoded.RawPayload);
    }
    [Fact]
    public void MissingOrUnsupportedFieldsAndFinalStatusesFailClosed()
    {
        var payout = Payout(); payout.BankBin = null; Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
        payout = Payout(); payout.BankCode = "MOMO"; Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
        payout = Payout(); payout.Amount = 1.5m; Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
        payout = Payout(); payout.Status = PayoutStatus.Paid; Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
        payout = Payout(); payout.AccountNumber = new string('1', 20); Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
        payout = Payout(); payout.BankBin = "970407"; Assert.Throws<InvalidOperationException>(() => PayoutQrGenerator.Payload(payout));
    }
    [Fact]
    public void CrcMatchesStandardCcittFalseKnownVector()
    {
        var crc = typeof(PayoutQrGenerator).GetMethod("Crc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal((ushort)0x29B1, crc.Invoke(null, ["123456789"]));
    }
}
