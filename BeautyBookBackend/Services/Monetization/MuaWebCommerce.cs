using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class MuaEmailCommerce
{
    public async Task<Guid> WebBuy(Guid owner, string sku, string key, string terms, bool consent, CancellationToken ct)
    {
        if (sku != MuaCommerceCatalog.Visibility) throw new CommerceException("PRODUCT_RETIRED_OR_INVALID");
        if (!options.Value.Enabled || !options.Value.WebPortalEnabled || !options.Value.WebCheckoutEnabled || !options.Value.PaymentLinksEnabled || !options.Value.WorkersEnabled)
            throw new CommerceException("WEB_CHECKOUT_DISABLED");
        if (!consent) throw new CommerceException("PORTFOLIO_CONSENT_REQUIRED");
        await Recipient(owner, ct);
        // A live pending link must be resolved/cancelled before replacing it, even
        // across devices or a lost browser idempotency key.
        var pending = await db.MuaCommerceOrders.AsNoTracking().Where(x => x.MuaId == owner && x.ProductId == sku && x.State == MuaCommerceOrderState.PaymentPending).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (pending != null)
        {
            if (pending.Origin != "IndependentWebsite" || !pending.PortfolioPromotionConsent || pending.TermsVersion != terms) throw new CommerceException("PENDING_ORDER_NEEDS_REVIEW");
            return pending.Id;
        }
        var order = await new MuaCommerceFoundation(db, clock).CreateOrderAsync(owner, sku, key, terms, ct, "IndependentWebsite", payos.Channel);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await MuaAccessPolicy.Lock(db, owner, ct);
        order.PortfolioPromotionConsent = true;
        await Enqueue("Link", order.Id, "link:" + order.Id.ToString("N"), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return order.Id;
    }
    public async Task<object?> WebOrder(Guid owner, Guid id, CancellationToken ct)
    {
        var order = await db.MuaCommerceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.MuaId == owner, ct);
        if (order == null) return null;
        var available = order.ProductId == MuaCommerceCatalog.Visibility && options.Value.WebCheckoutEnabled && options.Value.PaymentLinksEnabled && order.State == MuaCommerceOrderState.PaymentPending && order.ExpiresAt > Now;
        if (available) { try { await Recipient(owner, ct); } catch (CommerceException) { available = false; } }
        var url = available ? await db.MuaCommerceAttempts.Where(x => x.OrderId == id).Select(x => x.CheckoutUrl).SingleAsync(ct) : null;
        return new { order.Id, order.ProductId, order.AmountVnd, order.Currency, state = order.State.ToString(), order.CreatedAt, order.ExpiresAt, order.VerifiedPaidAt,
            checkoutAvailable = available && url != null, checkoutUrl = url };
    }
    public async Task<object> WebHistory(Guid owner, CancellationToken ct) => await db.MuaCommerceOrders.AsNoTracking().Where(x => x.MuaId == owner)
        .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.ProductId, x.AmountVnd, state = x.State.ToString(), x.CreatedAt, x.VerifiedPaidAt }).Take(100).ToListAsync(ct);
    public async Task Revision(Guid owner, Guid id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new CommerceException("INVALID_REVISION");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await MuaAccessPolicy.Lock(db, owner, ct);
        var task = await db.MuaFacebookTasks.SingleOrDefaultAsync(x => x.Id == id && x.MuaId == owner, ct) ?? throw new CommerceException("TASK_NOT_FOUND");
        if (task.Revisions >= 1) throw new CommerceException("REVISION_ALREADY_USED");
        task.Revisions++; task.RevisionRequest = reason; task.State = "RevisionRequested";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task CreateFacebookTask(MuaCommerceOrder order, CancellationToken ct)
    {
        if (!order.PortfolioPromotionConsent) throw new CommerceException("PORTFOLIO_CONSENT_REQUIRED");
        var grant = order.ProductId == MuaCommerceCatalog.Activation
            ? await db.MuaVisibilityGrants.SingleAsync(x => x.MuaId == order.MuaId && x.Kind == VisibilityGrantKind.FirstActivationGift, ct)
            : await db.MuaVisibilityGrants.SingleAsync(x => x.SourceReference == "payos:" + order.Id.ToString("N"), ct);
        if (await db.MuaFacebookTasks.AnyAsync(x => x.GrantId == grant.Id, ct)) return;
        db.MuaFacebookTasks.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, GrantId = grant.Id, OrderId = order.Id,
            CycleStartsAt = grant.StartsAt, DueAt = WorkingDeadline(grant.StartsAt), State = grant.StartsAt > Now ? "Scheduled" : "Preparing", CreatedAt = Now });
    }
    public static DateTime WorkingDeadline(DateTime start)
    {
        // The operation runs in Vietnam; weekend checks must use UTC+7 dates.
        var local = DateTime.SpecifyKind(start, DateTimeKind.Utc).AddHours(7); var days = 0;
        while (days < 3) { local = local.AddDays(1); if (local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++; }
        return DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
    }
    private async Task SendApproval(MuaCommerceJob job, CancellationToken ct)
    {
        if (!options.Value.EmailDeliveryEnabled) throw new CommerceException("EMAIL_DELIVERY_DISABLED");
        var user = await Recipient(job.TargetId, ct);
        // A message links to the account center, never to a provider checkout.
        var origin = config["MuaWeb:Origin"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.IsLoopback) || uri.UserInfo.Length != 0) throw new CommerceException("WEB_ORIGIN_MISSING");
        var hash = PayOsCommerceProtocol.Hash(user.Email!.Trim().ToLowerInvariant());
        if (job.RecipientHash != null && job.RecipientHash != hash) throw new CommerceException("RECIPIENT_CHANGED");
        if (job.ProviderMessageId != null) return;
        if (job.FirstSendAt.HasValue && Now - job.FirstSendAt.Value > TimeSpan.FromMinutes(10)) throw new CommerceException("EMAIL_DELIVERY_UNCERTAIN");
        job.FirstSendAt ??= Now; job.RecipientHash = hash; await db.SaveChangesAsync(ct);
        var html = $"<!doctype html><html lang=vi><meta charset=utf-8><body style='font-family:Arial;background:#fff5f8;padding:24px;color:#301726'><main style='max-width:520px;margin:auto;background:white;padding:28px;border-radius:16px'><h1 style='color:#c93462'>BBook</h1><p>Xin chào {H(user.FullName ?? "MUA")},</p><p>Hồ sơ MUA của bạn đã được duyệt. Bạn có thể đăng nhập trung tâm tài khoản BBook để xem trạng thái và gói quảng bá.</p><p><a href='{H(origin!.TrimEnd('/') + "/mua/")}'>Xem trạng thái và gói quảng bá</a></p><p>Email này không chứa số tài khoản hoặc mã QR thanh toán.</p><p>Hỗ trợ: {H(config["CommerceEmail:SupportEmail"] ?? "bbooksupport@gmail.com")}</p></main></body></html>";
        job.ProviderMessageId = await email.Send(new(job.Id, user.Email!, "BBook — Hồ sơ MUA đã được duyệt", html), ct); job.DeliveryState = "accepted";
        await db.SaveChangesAsync(ct);
    }
}
