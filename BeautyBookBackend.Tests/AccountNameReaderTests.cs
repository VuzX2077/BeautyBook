using BeautyBookBackend.Services;

namespace BeautyBookBackend.Tests;

public sealed class AccountNameReaderTests
{
    private static string Tsv(params (string Text, int Top, int Confidence)[] lines) => string.Join('\n', lines.Select((line, i) => $"5\t1\t{i + 1}\t1\t1\t1\t100\t{line.Top}\t300\t24\t{line.Confidence}\t{line.Text}"));

    [Theory]
    [InlineData("NGUYỄN VĂN TEST", "*******244", null)]
    [InlineData("NGUYEN VAN TEST", "1234567890", "1234567890")]
    [InlineData("Tên chủ tài khoản: NGUYEN VAN TEST", "1234567890", "1234567890")]
    public void Reads_printed_name_above_account_or_masked_momo_number(string name, string number, string? account)
    {
        var text = Tsv(("VIETCOMBANK", 50, 99), ("VIETQR NAPAS", 300, 95), (name, 500, 95), (number, 535, 90));
        Assert.Equal("NGUYEN VAN TEST", AccountNameReader.ExtractName(text, account));
    }

    [Fact]
    public void Does_not_choose_between_conflicting_names_or_accept_a_blurry_name()
    {
        Assert.Null(AccountNameReader.ExtractName(Tsv(("FIRST TEST USER", 100, 95), ("123456", 130, 90), ("OTHER TEST USER", 300, 95), ("123456", 330, 90)), "123456"));
        Assert.Null(AccountNameReader.ExtractName(Tsv(("BLURRY TEST USER", 100, 35), ("123456", 130, 90)), "123456"));
        Assert.Null(AccountNameReader.ExtractName(Tsv(("NGAN HANG VIET NAM", 100, 99), ("123456", 130, 90)), "123456"));
    }

    [Fact]
    public void Qr_identity_uses_receiver_instead_of_image_or_amount_and_never_invents_a_phone()
    {
        var qr = new BankQrData("MOMO", "971025", null, null, "payload-one", "synthetic-receiver");
        var first = FinancialMediaService.RecipientIdentity(qr);
        Assert.StartsWith("QR:", first);
        Assert.True(first.Length <= 30);
        Assert.Equal(first, FinancialMediaService.RecipientIdentity(qr with { RawPayload = "different-image-or-amount" }));
        Assert.NotEqual(first, FinancialMediaService.RecipientIdentity(qr with { RecipientIdentifier = "another-receiver" }));
        Assert.Equal("0300000000", FinancialMediaService.RecipientIdentity(qr with { AccountNumber = "84300000000" }));
    }
}
