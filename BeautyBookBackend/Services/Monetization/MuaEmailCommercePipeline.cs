using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class MuaEmailCommerce
{
    private async Task CreateLink(Guid id, CancellationToken ct)
    {
        if (!options.Value.PaymentLinksEnabled) throw new CommerceException("PAYMENT_LINKS_DISABLED");
        var order = await db.MuaCommerceOrders.SingleAsync(x => x.Id == id, ct);
        if (order.State != MuaCommerceOrderState.PaymentPending) return;
        if (order.ProductId == MuaCommerceCatalog.Activation) throw new CommerceException("ACTIVATION_RETIRED");
        if (order.Origin != "IndependentWebsite" || !order.PortfolioPromotionConsent) throw new CommerceException("EMAIL_CHECKOUT_RETIRED");
        await Recipient(order.MuaId, ct); await CheckOffer(order, ct);
        if (order.ExpiresAt <= Now) throw new CommerceException("LINK_EXPIRED");
        var attempt = await db.MuaCommerceAttempts.SingleAsync(x => x.OrderId == id, ct);
        if (attempt.Channel != payos.Channel) throw new CommerceException("PAYOS_CHANNEL_MISMATCH");
        if (attempt.PaymentLinkId == null)
        {
            JsonElement? response = null;
            var lookup = attempt.CreationStartedAt.HasValue;
            if (lookup) response = await payos.Get(attempt.OrderCode, ct);
            if (response == null)
            {
                attempt.CreationStartedAt ??= Now; await db.SaveChangesAsync(ct);
                response = await payos.Create(order, attempt.OrderCode, ct);
                lookup = false;
            }
            var data = payos.Verify(response.Value);
            var link = PayOsCommerceProtocol.Text(data, lookup ? "id" : "paymentLinkId");
            if (PayOsCommerceProtocol.Number(data, "orderCode") != attempt.OrderCode || PayOsCommerceProtocol.Number(data, "amount") != order.AmountVnd
                || !lookup && PayOsCommerceProtocol.Text(data, "currency") != "VND" || link.Length > 100)
                throw new CommerceException("PAYOS_LINK_MISMATCH");
            var status = PayOsCommerceProtocol.Text(data, "status");
            if (status is not ("PENDING" or "PAID")) throw new CommerceException("LINK_EXPIRED");
            var url = lookup ? "https://pay.payos.vn/web/" + Uri.EscapeDataString(link) : PayOsCommerceProtocol.Text(data, "checkoutUrl");
            if (!ValidCheckout(url)) throw new CommerceException("INVALID_CHECKOUT_URL");
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await MuaAccessPolicy.Lock(db, order.MuaId, ct);
                // A retried external operation can only bind the same provider link.
                await db.Entry(attempt).ReloadAsync(ct);
                if (attempt.PaymentLinkId != null && attempt.PaymentLinkId != link) throw new CommerceException("PAYOS_LINK_MISMATCH");
                attempt.PaymentLinkId = link; attempt.CheckoutUrl = url;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            if (status == "PAID") await ReconcileOrder(order.Id, ct);
        }
        // Checkout URL is read only through the authenticated website order endpoint.
    }

    private bool ValidCheckout(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
        && uri.IsDefaultPort && (uri.Host == "pay.payos.vn" || !payos.IsLive && uri.Host.EndsWith(".invalid", StringComparison.Ordinal));

    private async Task CheckOffer(MuaCommerceOrder order, CancellationToken ct)
    {
        if (order.Origin != "ExternalRequest") return;
        if (!Guid.TryParseExact(order.IdempotencyKey.Split(':').Last(), "N", out var id)) throw new CommerceException("OFFER_REVOKED");
        var offer = await db.MuaCommerceOffers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.MuaId == order.MuaId, ct);
        var currentEmail = await db.Users.Where(x => x.UserId == order.MuaId).Select(x => x.Email).SingleAsync(ct);
        if (offer == null || offer.RevokedAt != null || !string.Equals(offer.EmailSnapshot, currentEmail, StringComparison.OrdinalIgnoreCase)) throw new CommerceException("OFFER_REVOKED");
    }

    private async Task ApplyIngress(Guid id, CancellationToken ct)
    {
        var ingress = await db.MuaCommerceIngress.SingleAsync(x => x.Id == id, ct);
        if (ingress.State != "Pending") return;
        var attempt = await db.MuaCommerceAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.OrderCode == ingress.OrderCode && x.Channel == payos.Channel, ct);
        var order = attempt == null ? null : await db.MuaCommerceOrders.SingleOrDefaultAsync(x => x.Id == attempt.OrderId, ct);
        if (attempt != null && attempt.PaymentLinkId == null) throw new CommerceException("LINK_NOT_YET_BOUND");
        if (attempt == null || order == null || attempt.PaymentLinkId != ingress.PaymentLinkId || order.AmountVnd != ingress.AmountVnd)
        { ingress.State = "ManualReview"; await db.SaveChangesAsync(ct); return; }
        var deleted = !await db.Users.AnyAsync(x => x.UserId == order.MuaId && x.DeletedAt == null, ct);
        var extraPayment = ingress.Source == "Webhook" && await db.MuaCommerceIngress.AnyAsync(x => x.OrderCode == ingress.OrderCode && x.Id != id && x.Source == "Webhook" && x.State == "Recorded", ct);
        if (deleted || extraPayment) { ingress.State = "ManualReview"; await db.SaveChangesAsync(ct); return; }
        // Only fields that were authenticated at ingress are carried onward. Raw
        // payer/account fields are deliberately never persisted or logged.
        var data = JsonSerializer.SerializeToElement(new { orderCode = ingress.OrderCode, amount = ingress.AmountVnd, reference = ingress.Reference, paymentLinkId = ingress.PaymentLinkId, currency = "VND", code = "00" });
        await new MuaCommerceFoundation(db, clock).RecordAsync(data, payos.Channel, ingress.Source, ingress.PaymentLinkId, ingress.AmountVnd, ingress.Reference, ct);
        ingress.State = "Recorded";
        if (order.State == MuaCommerceOrderState.PaidAwaitingFulfillment)
            await Enqueue("Fulfill", order.Id, "fulfill:" + order.Id.ToString("N"), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task Fulfill(Guid id, CancellationToken ct)
    {
        var order = await db.MuaCommerceOrders.SingleAsync(x => x.Id == id, ct);
        if (order.State == MuaCommerceOrderState.Fulfilled) { await Enqueue("ReceiptEmail", id, "receipt:" + id.ToString("N"), ct); await db.SaveChangesAsync(ct); return; }
        if (order.State != MuaCommerceOrderState.PaidAwaitingFulfillment || order.VerifiedPaidAt == null) return;
        // Deletion is handled without touching owner-guarded financial rows.
        if (!await db.Users.AnyAsync(x => x.UserId == order.MuaId && x.DeletedAt == null, ct)) throw new CommerceException("MUA_NOT_ELIGIBLE");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await MuaAccessPolicy.Lock(db, order.MuaId, ct); await db.Entry(order).ReloadAsync(ct);
        if (order.State == MuaCommerceOrderState.Fulfilled) { await tx.CommitAsync(ct); return; }
        var profile = await db.MakeupArtistProfiles.Include(x => x.User).SingleAsync(x => x.MUAId == order.MuaId, ct);
        var account = await db.MuaMonetizationAccounts.SingleOrDefaultAsync(x => x.MuaId == order.MuaId, ct);
        var check = await eligibility.EvaluateAsync(order.MuaId, false);
        var source = "payos:" + order.Id.ToString("N");
        if (profile.User?.IsActive != true || profile.User.IsDemoAccount || profile.User.DeletedAt != null
            || profile.Status == MuaStatus.Suspended || profile.VerificationStatus != MuaVerificationStatus.Approved || check == null || check.MissingRequirements.Count != 0
            || order.ProductId == MuaCommerceCatalog.Activation && (account!.Cohort != MuaActivationCohort.PaidActivation || account.ActivationRevokedAt != null
                || account.ActivationPurchaseReference != null && account.ActivationPurchaseReference != source)
            || order.ProductId == MuaCommerceCatalog.Visibility && !MuaMonetizationPolicy.HasPermanentActivation(account))
        { order.State = MuaCommerceOrderState.ManualReview; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return; }
        var now = Now;
        var grants = await db.MuaVisibilityGrants.Where(x => x.MuaId == order.MuaId).ToListAsync(ct);
        if (order.ProductId == MuaCommerceCatalog.Activation)
        {
            account!.ActivatedAt ??= now; account.ActivationPurchaseReference ??= source;
            if (!grants.Any(x => x.Kind == VisibilityGrantKind.FirstActivationGift))
            {
                var interval = MuaMonetizationPolicy.NextVisibilityInterval(now, grants);
                db.MuaVisibilityGrants.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, Kind = VisibilityGrantKind.FirstActivationGift,
                    SourceReference = "gift:" + order.MuaId.ToString("N"), StartsAt = interval.StartsAt, EndsAt = interval.EndsAt, CreatedAt = now });
            }
        }
        else if (!grants.Any(x => x.SourceReference == source))
        {
            var interval = MuaMonetizationPolicy.NextVisibilityInterval(now, grants);
            db.MuaVisibilityGrants.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, Kind = VisibilityGrantKind.Paid,
                SourceReference = source, StartsAt = interval.StartsAt, EndsAt = interval.EndsAt, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        var published = await eligibility.EvaluateAsync(order.MuaId);
        if (published?.CanReceiveBookings != true)
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var failed = await db.MuaCommerceOrders.SingleAsync(x => x.Id == id, ct); failed.State = MuaCommerceOrderState.ManualReview;
            await db.SaveChangesAsync(ct); throw new CommerceException("MUA_NOT_ELIGIBLE");
        }
        if (order.ProductId == MuaCommerceCatalog.Activation) account!.FirstPublishedAt ??= now;
        await CreateFacebookTask(order, ct);
        order.State = MuaCommerceOrderState.Fulfilled;
        foreach (var held in await db.MuaCommerceOutbox.Where(x => x.OrderId == id && x.Kind == "VerifiedPaymentHold").ToListAsync(ct)) held.CompletedAt ??= now;
        db.MuaCommerceAudit.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, OrderId = id, Action = "Fulfilled", EvidenceHash = PayOsCommerceProtocol.Hash(source), CreatedAt = now });
        await Enqueue("ReceiptEmail", id, "receipt:" + id.ToString("N"), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task ReconcileOrder(Guid id, CancellationToken ct)
    {
        var order = await db.MuaCommerceOrders.SingleAsync(x => x.Id == id, ct);
        var attempt = await db.MuaCommerceAttempts.SingleAsync(x => x.OrderId == id, ct);
        var response = await payos.Get(attempt.OrderCode, ct);
        if (!response.HasValue) return;
        var data = payos.Verify(response.Value);
        if (PayOsCommerceProtocol.Number(data, "orderCode") != attempt.OrderCode || PayOsCommerceProtocol.Number(data, "amount") != order.AmountVnd
            || PayOsCommerceProtocol.Text(data, "id") != attempt.PaymentLinkId) throw new CommerceException("PAYMENT_MISMATCH");
        var status = PayOsCommerceProtocol.Text(data, "status");
        if (status == "PAID")
        {
            if (PayOsCommerceProtocol.Number(data, "amountPaid") != order.AmountVnd || PayOsCommerceProtocol.Number(data, "amountRemaining") != 0) throw new CommerceException("PAYMENT_MISMATCH");
            await StoreIngress(data, "Reconciliation", attempt.PaymentLinkId!, "reconcile:" + attempt.PaymentLinkId, ct);
        }
        else if (order.ExpiresAt <= Now && order.VerifiedPaidAt == null && order.State == MuaCommerceOrderState.PaymentPending)
        {
            if (status == "PENDING")
            {
                var cancel = payos.Verify(await payos.Cancel(attempt.OrderCode, ct));
                if (PayOsCommerceProtocol.Number(cancel, "orderCode") != attempt.OrderCode || PayOsCommerceProtocol.Text(cancel, "id") != attempt.PaymentLinkId
                    || PayOsCommerceProtocol.Text(cancel, "status") != "CANCELLED" || PayOsCommerceProtocol.Number(cancel, "amountPaid") != 0) throw new CommerceException("CANCEL_NOT_CONFIRMED");
            }
            else if (status is not ("CANCELLED" or "EXPIRED") || PayOsCommerceProtocol.Number(data, "amountPaid") != 0) throw new CommerceException("CANCEL_NOT_CONFIRMED");
            order.State = MuaCommerceOrderState.Cancelled; await db.SaveChangesAsync(ct);
        }
    }

    public async Task ReconcilePending(CancellationToken ct)
    {
        if (!options.Value.WorkersEnabled || !options.Value.PaymentLinksEnabled) return;
        var ids = await db.MuaCommerceOrders.AsNoTracking().Where(x => x.State == MuaCommerceOrderState.PaymentPending
            && db.MuaCommerceAttempts.Any(a => a.OrderId == x.Id && a.PaymentLinkId != null && a.Channel == payos.Channel))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids) { try { await ReconcileOrder(id, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } catch { db.ChangeTracker.Clear(); } }
    }

    public async Task Recover(Guid orderId, Guid admin, CancellationToken ct)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(admin);
        if (!await db.Users.AnyAsync(x => x.UserId == admin && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null, ct)) throw new CommerceException("ADMIN_REQUIRED");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.MuaCommerceOrders.SingleAsync(x => x.Id == orderId, ct); await MuaAccessPolicy.Lock(db, order.MuaId, ct);
        if (order.State != MuaCommerceOrderState.ManualReview || order.VerifiedPaidAt == null) throw new CommerceException("NO_VERIFIED_PAYMENT_TO_RECOVER");
        await Recipient(order.MuaId, ct); order.State = MuaCommerceOrderState.PaidAwaitingFulfillment;
        await Enqueue("Fulfill", orderId, "recovery:" + Guid.NewGuid().ToString("N"), ct);
        db.MuaCommerceAudit.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, OrderId = orderId, Action = "AdminRecovery", EvidenceHash = PayOsCommerceProtocol.Hash(admin.ToString()), CreatedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
