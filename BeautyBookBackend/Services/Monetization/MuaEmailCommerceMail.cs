using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class MuaEmailCommerce
{
    private async Task Send(MuaCommerceJob job, bool receipt, CancellationToken ct)
    {
        if (!options.Value.EmailDeliveryEnabled) throw new CommerceException("EMAIL_DELIVERY_DISABLED");
        var order = await db.MuaCommerceOrders.AsNoTracking().SingleAsync(x => x.Id == job.TargetId, ct);
        if (receipt ? order.State != MuaCommerceOrderState.Fulfilled : order.State != MuaCommerceOrderState.PaymentPending) return;
        var user = await Recipient(order.MuaId, ct, requireVerifiedEmail: true);
        if (!receipt) { await CheckOffer(order, ct); if (order.ExpiresAt <= Now) throw new CommerceException("LINK_EXPIRED"); }
        var hash = PayOsCommerceProtocol.Hash(user.Email!.Trim().ToLowerInvariant());
        if (job.RecipientHash != null && job.RecipientHash != hash) throw new CommerceException("RECIPIENT_CHANGED");
        if (await db.MuaCommerceJobs.AnyAsync(x => x.RecipientHash == hash && (x.DeliveryState == "hard_bounce" || x.DeliveryState == "spam" || x.DeliveryState == "blocked"), ct)) throw new CommerceException("EMAIL_SUPPRESSED");
        if (job.ProviderMessageId != null) return;
        // Provider idempotency has a bounded TTL, not an exactly-once delivery
        // guarantee. Stop automatic retries well before its documented expiry.
        if (job.FirstSendAt.HasValue && Now - job.FirstSendAt.Value > TimeSpan.FromMinutes(10)) throw new CommerceException("EMAIL_DELIVERY_UNCERTAIN");
        var product = order.ProductId == MuaCommerceCatalog.Activation ? new MuaCommerceProduct(MuaCommerceCatalog.Activation, "Kích hoạt MUA (gói cũ)", order.AmountVnd, null) : MuaCommerceCatalog.Get(order.ProductId);
        var activation = order.ProductId == MuaCommerceCatalog.Activation;
        string? url = null; DateTime? ends = null;
        if (!receipt)
        {
            var attempt = await db.MuaCommerceAttempts.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, ct);
            url = attempt.CheckoutUrl;
            if (url == null || !ValidCheckout(url)) throw new CommerceException("INVALID_CHECKOUT_URL");
        }
        else
        {
            var grants = await db.MuaVisibilityGrants.AsNoTracking().Where(x => x.MuaId == order.MuaId && x.RevokedAt == null).ToListAsync(ct);
            var grant = activation ? grants.SingleOrDefault(x => x.Kind == VisibilityGrantKind.FirstActivationGift)
                : grants.SingleOrDefault(x => x.SourceReference == "payos:" + order.Id.ToString("N"));
            var account = await db.MuaMonetizationAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.MuaId == order.MuaId, ct);
            if (grant == null || !MuaMonetizationPolicy.HasPermanentActivation(account)) throw new CommerceException("ENTITLEMENT_RECEIPT_MISMATCH");
            ends = grant.EndsAt;
        }
        var subject = receipt ? activation ? "BBook — Tài khoản MUA đã được kích hoạt" : "BBook Visibility — Xác nhận gói quảng bá 30 ngày"
            : activation ? "BBook — Hồ sơ MUA đã được duyệt" : "BBook Visibility — Thông tin gói quảng bá 30 ngày";
        var text = receipt ? activation ? "Tài khoản đã được kích hoạt lâu dài. Quyền nhận booking đã mở; hồ sơ được xuất bản khi đáp ứng điều kiện hoạt động. Quà Visibility 30 ngày đã được cấp một lần."
            : "Gói Visibility 30 ngày đã được cấp. Thời gian được nối tiếp vào thời gian quảng bá hợp lệ hiện có."
            : activation ? "Hồ sơ của bạn đã được duyệt. Phí kích hoạt 100.000đ/lần mở quyền xuất bản hồ sơ và nhận booking, kèm quà quảng bá 30 ngày một lần. Khi chưa kích hoạt, hồ sơ chưa được xuất bản theo chính sách tài khoản trả phí."
            : "Theo yêu cầu hoặc sự đồng ý độc lập của bạn, BBook cung cấp gói quảng bá 75.000đ/30 ngày. Đây là lượt mua chủ động, không tự động gia hạn hoặc trừ tiền.";
        var extra = receipt ? $"Thời hạn quyền quảng bá đã cấp: {Local(ends!.Value)}. Mã tham chiếu: {order.Id:N}."
            : $"Link thanh toán có hiệu lực đến {Local(order.ExpiresAt)}. Giao dịch chỉ kích hoạt tài khoản BBook sở hữu đơn, kể cả khi email được chuyển tiếp.";
        var unsubscribe = "";
        if (!receipt && order.Origin == "ExternalRequest")
        {
            var offerId = Guid.ParseExact(order.IdempotencyKey.Split(':').Last(), "N");
            var offer = await db.MuaCommerceOffers.AsNoTracking().SingleAsync(x => x.Id == offerId, ct);
            if (offer.Kind == "MarketingOptIn")
            {
                subject = "[QC] " + subject;
                var origin = config["CommerceEmail:PublicBackendOrigin"];
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new CommerceException("UNSUBSCRIBE_CONFIG_MISSING");
                var token = OfferToken(offerId);
                unsubscribe = $"<p><a href=\"{H(origin!.TrimEnd('/') + "/api/mua/commerce/unsubscribe?token=" + Uri.EscapeDataString(token))}\">Từ chối nhận đề nghị này</a></p>";
            }
        }
        var support = config["CommerceEmail:SupportEmail"] ?? "bbooksupport@gmail.com";
        if (!System.Net.Mail.MailAddress.TryCreate(support, out _)) throw new CommerceException("INVALID_SUPPORT_EMAIL");
        var button = url == null ? "" : $"<p><a href=\"{H(url)}\" style=\"display:inline-block;background:#c93462;color:white;padding:14px 20px;border-radius:8px;text-decoration:none\">Thanh toán qua PayOS</a></p>";
        var html = $$"""
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;background:#fff5f8;font-family:Arial,sans-serif;color:#301726"><table role="presentation" width="100%"><tr><td align="center" style="padding:24px 12px">
        <table role="presentation" width="100%" style="max-width:540px;background:white;border:1px solid #f0d6df;border-radius:16px"><tr><td style="padding:28px;line-height:1.7">
        <h1 style="color:#c93462">BBook</h1><p>Xin chào {{H(user.FullName ?? "MUA")}},</p><h2 style="font-size:20px">{{H(product.Name)}}</h2>
        <p><strong>{{product.AmountVnd.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}} VND</strong></p><p>{{H(text)}}</p>{{button}}<p>{{H(extra)}}</p>
        <p>Quảng bá không bảo đảm số booking hoặc doanh thu. Hết Visibility vẫn nhận booking thông thường nếu tài khoản đủ điều kiện hoạt động.</p>
        <p>Hỗ trợ: {{H(support)}}</p>{{unsubscribe}}</td></tr></table></td></tr></table></body></html>
        """;
        job.FirstSendAt ??= Now; job.RecipientHash = hash; await db.SaveChangesAsync(ct);
        job.ProviderMessageId = await email.Send(new(job.Id, user.Email!, subject, html), ct);
        job.DeliveryState ??= "accepted"; await db.SaveChangesAsync(ct);
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);
    private static string Local(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm 'UTC+7'");
    private string OfferToken(Guid id)
    {
        var key = config["CommerceEmail:UnsubscribeSecret"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw new CommerceException("UNSUBSCRIBE_CONFIG_MISSING");
        return id.ToString("N") + "." + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(id.ToString("N"))));
    }
    public async Task Unsubscribe(string token, CancellationToken ct)
    {
        var parts = token.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id)) throw new CommerceException("INVALID_UNSUBSCRIBE_TOKEN");
        try { if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), Convert.FromHexString(OfferToken(id).Split('.')[1]))) throw new CommerceException("INVALID_UNSUBSCRIBE_TOKEN"); }
        catch (FormatException) { throw new CommerceException("INVALID_UNSUBSCRIBE_TOKEN"); }
        var offer = await db.MuaCommerceOffers.SingleOrDefaultAsync(x => x.Id == id && x.Kind == "MarketingOptIn", ct);
        if (offer != null) { offer.RevokedAt ??= Now; await db.SaveChangesAsync(ct); }
    }

    public async Task Delivery(string messageId, string recipient, string state, string? tag, CancellationToken ct)
    {
        var job = await db.MuaCommerceJobs.SingleOrDefaultAsync(x => x.ProviderMessageId == messageId, ct);
        if (job == null && tag?.StartsWith("commerce-job:", StringComparison.Ordinal) == true && Guid.TryParse(tag[13..], out var id))
            job = await db.MuaCommerceJobs.SingleOrDefaultAsync(x => x.Id == id && x.FirstSendAt != null && (x.ProviderMessageId == null || x.ProviderMessageId == messageId), ct);
        if (job == null || string.IsNullOrWhiteSpace(messageId) || messageId.Length > 300) throw new CommerceException("UNKNOWN_EMAIL_MESSAGE");
        if (job.RecipientHash != PayOsCommerceProtocol.Hash(recipient.Trim().ToLowerInvariant())) throw new CommerceException("EMAIL_RECIPIENT_MISMATCH");
        if (state is not ("delivered" or "soft_bounce" or "hard_bounce" or "spam" or "blocked" or "invalid" or "deferred" or "request" or "sent")) throw new CommerceException("UNKNOWN_EMAIL_EVENT");
        job.ProviderMessageId = messageId;
        if (job.DeliveryState is not ("hard_bounce" or "spam" or "blocked")) job.DeliveryState = state;
        job.State = "Done"; job.LeaseUntil = null; await db.SaveChangesAsync(ct);
    }
}
