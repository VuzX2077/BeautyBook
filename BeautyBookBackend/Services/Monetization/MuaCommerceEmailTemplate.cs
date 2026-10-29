using System.Net;
using System.Globalization;

namespace BeautyBookBackend.Services.Monetization;

// Preview only. No sender or approval trigger calls this template. A paid receipt
// must eventually be sent from a durable order outbox, never from a client return URL.
public static class MuaCommerceEmailTemplate
{
    public static string ReceiptPreview(string muaName, string productId, string supportEmail)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(supportEmail, out _)) throw new CommerceException("INVALID_SUPPORT_EMAIL");
        var product = MuaCommerceCatalog.Get(productId);
        var name = WebUtility.HtmlEncode(muaName);
        var title = WebUtility.HtmlEncode(product.Name);
        var support = WebUtility.HtmlEncode(supportEmail);
        var price = product.AmountVnd.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var benefits = product.Days.HasValue ? "Quảng bá 30 ngày. Không tự động gia hạn. Hết hạn vẫn nhận booking thông thường."
            : "AI quét mặt: thanh toán 20.000đ một lần, quyền sử dụng vĩnh viễn theo tài khoản BBook.";
        return $$"""
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
        <body style="margin:0;background:#fff5f8;color:#301726;font-family:Arial,sans-serif">
        <table role="presentation" width="100%"><tr><td align="center" style="padding:24px 12px">
        <table role="presentation" width="100%" style="max-width:520px;background:white;border:1px solid #f0d6df;border-radius:16px"><tr><td style="padding:28px;line-height:1.6">
        <h1 style="color:#c93462;font-size:26px">BBook</h1><p><strong>BẢN XEM TRƯỚC — CHƯA GỬI</strong></p>
        <p>Xin chào {{name}},</p><p>Sản phẩm: <strong>{{title}}</strong><br>Giá: <strong>{{price}} VND</strong></p>
        <p>{{benefits}}</p><p>Quảng bá không bảo đảm có booking hoặc doanh thu.</p><p>Hỗ trợ: {{support}}</p>
        </td></tr></table></td></tr></table></body></html>
        """;
    }
}
