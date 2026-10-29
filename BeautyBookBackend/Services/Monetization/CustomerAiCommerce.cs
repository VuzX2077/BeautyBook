using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class CustomerAiCommerce(ApplicationDbContext db, ICommercePayOs payos, IConfiguration config, TimeProvider clock, ILogger<CustomerAiCommerce>? logger = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public bool CheckoutEnabled => config.GetValue<bool>("CustomerAi:WebCheckoutEnabled");
    public bool CheckoutReady => CheckoutEnabled && (!payos.IsLive || CommerceCheckoutReadiness.Ready(config));
    public bool EnforcementEnabled => config.GetValue<bool>("CustomerAi:EnforcementEnabled");
    public bool ReconciliationEnabled => config.GetValue<bool>("CustomerAi:ReconciliationEnabled");
    private bool ReadsEnabled => CheckoutEnabled || EnforcementEnabled || ReconciliationEnabled;
    private async Task<User> Customer(Guid owner, CancellationToken ct) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Role == UserRole.Customer && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct)
        ?? throw new CommerceException("CUSTOMER_REQUIRED");
    public async Task<bool> Allowed(Guid owner, CancellationToken ct)
    {
        if (!EnforcementEnabled) return true;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null, ct);
        if (user == null) return false;
        if (user.Role != UserRole.Customer) return true;
        return !user.IsDemoAccount && await db.CustomerAiPurchases.AnyAsync(x => x.UserId == owner && x.PaidAt != null && x.RevokedAt == null && x.State == "Fulfilled", ct);
    }
    public async Task<object> Status(Guid owner, CancellationToken ct)
    {
        var active = ReadsEnabled && await db.CustomerAiPurchases.AnyAsync(x => x.UserId == owner && x.PaidAt != null && x.RevokedAt == null && x.State == "Fulfilled", ct);
        return new { productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, lifetime = true, hasAccess = await Allowed(owner, ct), purchased = active, checkoutEnabled = CheckoutReady, enforcementEnabled = EnforcementEnabled };
    }
    private async Task Lock(Guid owner, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(724266524669002)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"customer-ai:" + owner}, 772028))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"UserId\"={owner} FOR UPDATE", ct);
    }
    public async Task<Guid> Buy(Guid owner, string key, string terms, CancellationToken ct)
    {
        if (!CheckoutReady) throw new CommerceException("CUSTOMER_AI_CHECKOUT_DISABLED");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || key != key.Trim()) throw new CommerceException("INVALID_IDEMPOTENCY_KEY");
        if (terms != MuaCommerceCatalog.TermsVersion) throw new CommerceException("TERMS_VERSION_MISMATCH");
        await Customer(owner, ct);
        var previous = await db.CustomerAiPurchases.AsNoTracking().Where(x => x.UserId == owner && x.State == "PaymentPending").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (previous != null) await Refresh(previous.Id, ct);
        CustomerAiPurchase row;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(owner, ct); await Customer(owner, ct);
            if (await db.CustomerAiPurchases.AnyAsync(x => x.UserId == owner && x.PaidAt != null && x.RevokedAt == null, ct)) throw new CommerceException("AI_ALREADY_PURCHASED");
            row = await db.CustomerAiPurchases.SingleOrDefaultAsync(x => x.UserId == owner && x.IdempotencyKey == key, ct)
                ?? await db.CustomerAiPurchases.Where(x => x.UserId == owner && x.State == "PaymentPending").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct)
                ?? new() { Id = Guid.NewGuid(), UserId = owner, IdempotencyKey = key, Channel = payos.Channel,
                    OrderCode = 4000000000000L + RandomNumberGenerator.GetInt32(1, int.MaxValue), CreatedAt = Now, ExpiresAt = Now.AddMinutes(30) };
            if (db.Entry(row).State == EntityState.Detached) db.CustomerAiPurchases.Add(row);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        if (row.State == "PaymentPending") await EnsureLink(row, ct);
        await Refresh(row.Id, ct);
        return row.Id;
    }
    private static void Match(JsonElement data, CustomerAiPurchase row, string linkField)
    {
        var link = PayOsCommerceProtocol.Text(data, linkField);
        if (PayOsCommerceProtocol.Number(data, "orderCode") != row.OrderCode || PayOsCommerceProtocol.Number(data, "amount") != 20000
            || link.Length > 100 || row.PaymentLinkId != null && row.PaymentLinkId != link
            || data.TryGetProperty("currency", out var currency) && currency.GetString() != "VND") throw new CommerceException("PAYMENT_MISMATCH");
    }
    private async Task EnsureLink(CustomerAiPurchase row, CancellationToken ct)
    {
        if (row.Channel != payos.Channel) throw new CommerceException("PAYOS_CHANNEL_MISMATCH");
        if (row.PaymentLinkId != null) return;
        var found = row.LinkCreationStartedAt.HasValue ? await payos.Get(row.OrderCode, ct) : null;
        if (found == null && row.ExpiresAt <= Now) { row.State = "Cancelled"; await db.SaveChangesAsync(ct); return; }
        if (found == null) { row.LinkCreationStartedAt ??= Now; await db.SaveChangesAsync(ct); }
        var response = found ?? await payos.Create(new MuaCommerceOrder { Id = row.Id, MuaId = row.UserId, ProductId = MuaCommerceCatalog.CustomerAi, AmountVnd = 20000, ExpiresAt = row.ExpiresAt }, row.OrderCode, ct);
        var data = payos.Verify(response); var lookup = found.HasValue;
        Match(data, row, lookup ? "id" : "paymentLinkId");
        var link = PayOsCommerceProtocol.Text(data, lookup ? "id" : "paymentLinkId");
        var status = PayOsCommerceProtocol.Text(data, "status");
        if (status is not ("PENDING" or "PAID" or "CANCELLED" or "EXPIRED")) throw new CommerceException("INVALID_PAYMENT_STATE");
        var url = lookup ? "https://pay.payos.vn/web/" + Uri.EscapeDataString(link) : PayOsCommerceProtocol.Text(data, "checkoutUrl");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || (uri.Host != "pay.payos.vn" && (payos.IsLive || !uri.Host.EndsWith(".invalid", StringComparison.Ordinal)))) throw new CommerceException("INVALID_CHECKOUT_URL");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(row.UserId, ct); await db.Entry(row).ReloadAsync(ct);
        if (row.PaymentLinkId != null && row.PaymentLinkId != link) throw new CommerceException("PAYMENT_MISMATCH");
        row.PaymentLinkId = link; row.CheckoutUrl = url;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task Refresh(Guid id, CancellationToken ct)
    {
        var row = await db.CustomerAiPurchases.SingleAsync(x => x.Id == id, ct);
        if (row.State != "PaymentPending") return;
        await EnsureLink(row, ct);
        if (row.State != "PaymentPending") return;
        var response = await payos.Get(row.OrderCode, ct);
        if (!response.HasValue) return;
        var data = payos.Verify(response.Value); Match(data, row, "id");
        var status = PayOsCommerceProtocol.Text(data, "status");
        if (status == "PAID")
        {
            if (PayOsCommerceProtocol.Number(data, "amountPaid") != 20000 || PayOsCommerceProtocol.Number(data, "amountRemaining") != 0) throw new CommerceException("PAYMENT_MISMATCH");
            await Apply(data, false, ct);
        }
        else if (row.ExpiresAt <= Now || status is "CANCELLED" or "EXPIRED")
        {
            if (status == "PENDING") data = payos.Verify(await payos.Cancel(row.OrderCode, ct));
            Match(data, row, "id");
            if (PayOsCommerceProtocol.Text(data, "status") is not ("CANCELLED" or "EXPIRED") || PayOsCommerceProtocol.Number(data, "amountPaid") != 0) throw new CommerceException("CANCEL_NOT_CONFIRMED");
            await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(row.UserId, ct); await db.Entry(row).ReloadAsync(ct);
            if (row.PaidAt == null && row.State == "PaymentPending") { row.State = "Cancelled"; await db.SaveChangesAsync(ct); }
            await tx.CommitAsync(ct);
        }
    }
    public async Task<object?> Order(Guid owner, Guid id, CancellationToken ct)
    {
        if (!ReadsEnabled) return null;
        // MUA order IDs must fall through to MUA commerce without requiring a Customer role.
        var row = await db.CustomerAiPurchases.SingleOrDefaultAsync(x => x.Id == id && x.UserId == owner, ct);
        if (row == null) return null;
        await Customer(owner, ct); if (!payos.IsLive || CommerceCheckoutReadiness.Ready(config)) await Refresh(id, ct);
        return new { row.Id, productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, currency = "VND", row.State, row.CreatedAt, row.ExpiresAt, verifiedPaidAt = row.PaidAt,
            checkoutAvailable = CheckoutReady && row.State == "PaymentPending" && row.ExpiresAt > Now && row.CheckoutUrl != null,
            checkoutUrl = CheckoutReady && row.State == "PaymentPending" && row.ExpiresAt > Now ? row.CheckoutUrl : null };
    }
    public async Task<object> History(Guid owner, CancellationToken ct) => !ReadsEnabled ? Array.Empty<object>() :
        await db.CustomerAiPurchases.AsNoTracking().Where(x => x.UserId == owner).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, x.State, x.CreatedAt, verifiedPaidAt = x.PaidAt }).ToListAsync(ct);
    public async Task<bool> Receive(JsonElement envelope, CancellationToken ct)
    {
        if (!ReadsEnabled) return false;
        var data = payos.Verify(envelope); var code = PayOsCommerceProtocol.Number(data, "orderCode");
        if (!await db.CustomerAiPurchases.AnyAsync(x => x.OrderCode == code, ct)) return false;
        if (PayOsCommerceProtocol.Text(data, "code") != "00" || PayOsCommerceProtocol.Text(data, "currency") != "VND" || PayOsCommerceProtocol.Number(data, "amount") != 20000) throw new CommerceException("INVALID_PAYMENT");
        var link = PayOsCommerceProtocol.Text(data, "paymentLinkId");
        var reference = data.TryGetProperty("reference", out var referenceValue) ? referenceValue.GetString() ?? "" : "";
        if (link.Length > 100 || reference.Length > 200) throw new CommerceException("INVALID_PAYMENT_REFERENCE");
        var key = PayOsCommerceProtocol.Hash($"customer-ai:{payos.Channel}:{code}:{reference}:{link}");
        var proof = PayOsCommerceProtocol.Hash(PayOsSignature.Canonicalize(data));
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 772029))", ct);
            var prior = await db.MuaCommerceIngress.SingleOrDefaultAsync(x => x.EventKey == key, ct);
            if (prior != null && prior.EvidenceHash != proof) throw new CommerceException("EVENT_REPLAY_MISMATCH");
            if (prior == null) db.MuaCommerceIngress.Add(new() { Id = Guid.NewGuid(), EventKey = key, EvidenceHash = proof, OrderCode = code, AmountVnd = 20000,
                PaymentLinkId = link, Reference = reference, Source = "CustomerAiWebhook", ReceivedAt = Now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await ProcessInbox(ct); return true;
    }
    public async Task ReconcilePending(Guid? owner, CancellationToken ct)
    {
        if (!ReadsEnabled) return;
        await ProcessInbox(ct, owner);
        var ids = await db.CustomerAiPurchases.AsNoTracking().Where(x => (!owner.HasValue || x.UserId == owner) && x.State == "PaymentPending"
            && db.Users.Any(u => u.UserId == x.UserId && u.IsActive && u.DeletedAt == null && !u.IsDemoAccount && u.Role == UserRole.Customer))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(owner.HasValue ? 5 : 30).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await Refresh(id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (!owner.HasValue)
            {
                db.ChangeTracker.Clear();
                logger?.LogWarning("Customer AI reconciliation deferred order {OrderId}: {ErrorType}", id, ex.GetType().Name);
            }
        }
    }
    private async Task ProcessInbox(CancellationToken ct, Guid? owner = null)
    {
        var rows = await db.MuaCommerceIngress.Where(x => x.Source == "CustomerAiWebhook" && x.State == "Pending" && (!owner.HasValue || db.CustomerAiPurchases.Any(p => p.OrderCode == x.OrderCode && p.UserId == owner))).OrderBy(x => x.ReceivedAt).Take(30).ToListAsync(ct);
        foreach (var ingress in rows)
        {
            var row = await db.CustomerAiPurchases.AsNoTracking().SingleOrDefaultAsync(x => x.OrderCode == ingress.OrderCode, ct);
            if (row == null || !await db.Users.AnyAsync(u => u.UserId == row.UserId && u.IsActive && u.DeletedAt == null && !u.IsDemoAccount && u.Role == UserRole.Customer, ct))
                ingress.State = "ManualReview";
            else if (row.PaymentLinkId == null) continue;
            else if (row.PaymentLinkId != ingress.PaymentLinkId || row.Channel != payos.Channel || ingress.AmountVnd != 20000) ingress.State = "ManualReview";
            else
            {
                var data = JsonSerializer.SerializeToElement(new { orderCode = ingress.OrderCode, amount = ingress.AmountVnd, paymentLinkId = ingress.PaymentLinkId, currency = "VND" });
                await Apply(data, true, ct); ingress.State = "Recorded";
            }
            await db.SaveChangesAsync(ct);
        }
    }
    private async Task Apply(JsonElement data, bool webhook, CancellationToken ct)
    {
        var code = PayOsCommerceProtocol.Number(data, "orderCode");
        var row = await db.CustomerAiPurchases.SingleAsync(x => x.OrderCode == code, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(row.UserId, ct); await db.Entry(row).ReloadAsync(ct);
        Match(data, row, webhook ? "paymentLinkId" : "id");
        if (row.Channel != payos.Channel) throw new CommerceException("PAYOS_CHANNEL_MISMATCH");
        if (row.PaidAt == null)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == row.UserId, ct);
            var already = await db.CustomerAiPurchases.AnyAsync(x => x.UserId == row.UserId && x.Id != row.Id && x.PaidAt != null && x.RevokedAt == null, ct);
            row.PaidAt = Now;
            row.State = row.State == "PaymentPending" && user is { IsActive: true, DeletedAt: null, IsDemoAccount: false, Role: UserRole.Customer } && !already && row.RevokedAt == null ? "Fulfilled" : "ManualReview";
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
