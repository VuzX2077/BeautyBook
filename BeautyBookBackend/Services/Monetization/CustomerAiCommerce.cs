using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed class CustomerAiCommerce(ApplicationDbContext db, ICommercePayOs payos, IConfiguration config, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public bool CheckoutEnabled => config.GetValue<bool>("CustomerAi:WebCheckoutEnabled");
    public bool EnforcementEnabled => config.GetValue<bool>("CustomerAi:EnforcementEnabled");
    private async Task<User> Customer(Guid owner, CancellationToken ct) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Role == UserRole.Customer && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct)
        ?? throw new CommerceException("CUSTOMER_REQUIRED");
    public async Task<bool> Allowed(Guid owner, CancellationToken ct)
    {
        if (!EnforcementEnabled) return true;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null, ct);
        if (user == null) return false;
        if (!EnforcementEnabled || user.Role != UserRole.Customer) return true;
        return !user.IsDemoAccount && await db.CustomerAiPurchases.AnyAsync(x => x.UserId == owner && x.PaidAt != null && x.RevokedAt == null && x.State == "Fulfilled", ct);
    }
    public async Task<object> Status(Guid owner, CancellationToken ct)
    {
        // Disabled rollout must not query an unmigrated table.
        var active = EnforcementEnabled || CheckoutEnabled
            ? await db.CustomerAiPurchases.AnyAsync(x => x.UserId == owner && x.PaidAt != null && x.RevokedAt == null && x.State == "Fulfilled", ct) : false;
        return new { productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, lifetime = true, hasAccess = await Allowed(owner, ct), purchased = active, checkoutEnabled = CheckoutEnabled, enforcementEnabled = EnforcementEnabled };
    }
    private async Task Lock(Guid owner, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(724266524669002)", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"UserId\"={owner} FOR UPDATE", ct);
        }
    }
    public async Task<Guid> Buy(Guid owner, string key, string terms, CancellationToken ct)
    {
        if (!CheckoutEnabled) throw new CommerceException("CUSTOMER_AI_CHECKOUT_DISABLED");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || key != key.Trim()) throw new CommerceException("INVALID_IDEMPOTENCY_KEY");
        if (terms != MuaCommerceCatalog.TermsVersion) throw new CommerceException("TERMS_VERSION_MISMATCH");
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
        // Persist the provider code first. Retrying always looks up this same code.
        if (row.PaymentLinkId == null)
        {
            var response = await payos.Get(row.OrderCode, ct) ?? await payos.Create(new MuaCommerceOrder { Id = row.Id, MuaId = owner, ProductId = MuaCommerceCatalog.CustomerAi, AmountVnd = 20000, ExpiresAt = row.ExpiresAt }, row.OrderCode, ct);
            var data = payos.Verify(response);
            var lookup = data.TryGetProperty("id", out _);
            var link = PayOsCommerceProtocol.Text(data, lookup ? "id" : "paymentLinkId");
            if (PayOsCommerceProtocol.Number(data, "orderCode") != row.OrderCode || PayOsCommerceProtocol.Number(data, "amount") != 20000 || link.Length > 100)
                throw new CommerceException("PAYOS_LINK_MISMATCH");
            var url = lookup ? "https://pay.payos.vn/web/" + Uri.EscapeDataString(link) : PayOsCommerceProtocol.Text(data, "checkoutUrl");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || (uri.Host != "pay.payos.vn" && (payos.IsLive || !uri.Host.EndsWith(".invalid")))) throw new CommerceException("INVALID_CHECKOUT_URL");
            row.PaymentLinkId = link; row.CheckoutUrl = url;
            await db.SaveChangesAsync(ct);
        }
        await Order(owner, row.Id, ct);
        return row.Id;
    }
    public async Task<object?> Order(Guid owner, Guid id, CancellationToken ct)
    {
        if (!CheckoutEnabled && !EnforcementEnabled) return null;
        await Customer(owner, ct);
        var row = await db.CustomerAiPurchases.SingleOrDefaultAsync(x => x.Id == id && x.UserId == owner, ct);
        if (row == null) return null;
        if (row.State == "PaymentPending")
        {
            var response = await payos.Get(row.OrderCode, ct);
            if (response.HasValue)
            {
                var data = payos.Verify(response.Value);
                var status = PayOsCommerceProtocol.Text(data, "status");
                if (status == "PAID")
                {
                    if (PayOsCommerceProtocol.Number(data, "amountPaid") != 20000 || PayOsCommerceProtocol.Number(data, "amountRemaining") != 0) throw new CommerceException("PAYMENT_MISMATCH");
                    await Apply(data, false, ct);
                }
                else if (status is "CANCELLED" or "EXPIRED") { row.State = "Cancelled"; await db.SaveChangesAsync(ct); }
            }
        }
        return new { row.Id, productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, currency = "VND", row.State, row.CreatedAt, row.ExpiresAt, verifiedPaidAt = row.PaidAt,
            checkoutAvailable = CheckoutEnabled && row.State == "PaymentPending" && row.ExpiresAt > Now && row.CheckoutUrl != null,
            checkoutUrl = CheckoutEnabled && row.State == "PaymentPending" && row.ExpiresAt > Now ? row.CheckoutUrl : null };
    }
    public async Task<object> History(Guid owner, CancellationToken ct) => !CheckoutEnabled && !EnforcementEnabled ? Array.Empty<object>() :
        await db.CustomerAiPurchases.AsNoTracking().Where(x => x.UserId == owner).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, productId = MuaCommerceCatalog.CustomerAi, amountVnd = 20000, x.State, x.CreatedAt, verifiedPaidAt = x.PaidAt }).ToListAsync(ct);
    // Return false for MUA orders so the existing durable MUA ingress handles them.
    public async Task<bool> Receive(JsonElement envelope, CancellationToken ct)
    {
        if (!CheckoutEnabled && !EnforcementEnabled) return false;
        var data = payos.Verify(envelope);
        if (!await db.CustomerAiPurchases.AnyAsync(x => x.OrderCode == PayOsCommerceProtocol.Number(data, "orderCode"), ct)) return false;
        if (PayOsCommerceProtocol.Text(data, "code") != "00" || PayOsCommerceProtocol.Text(data, "currency") != "VND") throw new CommerceException("INVALID_PAYMENT");
        await Apply(data, true, ct); return true;
    }
    private async Task Apply(JsonElement data, bool webhook, CancellationToken ct)
    {
        var code = PayOsCommerceProtocol.Number(data, "orderCode");
        var row = await db.CustomerAiPurchases.SingleAsync(x => x.OrderCode == code, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(row.UserId, ct); await db.Entry(row).ReloadAsync(ct);
        var link = PayOsCommerceProtocol.Text(data, webhook ? "paymentLinkId" : "id");
        if (row.Channel != payos.Channel || row.PaymentLinkId != link || PayOsCommerceProtocol.Number(data, "amount") != 20000) throw new CommerceException("PAYMENT_MISMATCH");
        if (row.PaidAt == null)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == row.UserId, ct);
            var already = await db.CustomerAiPurchases.AnyAsync(x => x.UserId == row.UserId && x.Id != row.Id && x.PaidAt != null && x.RevokedAt == null, ct);
            row.PaidAt = Now;
            row.State = user is { IsActive: true, DeletedAt: null, IsDemoAccount: false, Role: UserRole.Customer } && !already && row.RevokedAt == null ? "Fulfilled" : "ManualReview";
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
