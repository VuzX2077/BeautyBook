using System.Text.RegularExpressions;
namespace BeautyBookBackend.Services;
public static class MomoPhone
{
    public static string Normalize(string? input)
    {
        var phone = (input ?? "").Trim();
        if (phone.StartsWith("+84", StringComparison.Ordinal)) phone = phone[1..];
        if (Regex.IsMatch(phone, "^84[35789][0-9]{8}$")) phone = "0" + phone[2..];
        if (!Regex.IsMatch(phone, "^0[35789][0-9]{8}$")) throw new BookingRuleException("BANK_ACCOUNT_INVALID", "Số MoMo phải là số di động Việt Nam hợp lệ (10 chữ số hoặc +84/84).", 400);
        return phone;
    }
}
