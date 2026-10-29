using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

// Local foundation, deliberately not registered in Program.cs and not exposed by a
// controller. Tests supply synthetic accounts and signed fake PayOS responses.
// This service records verified money and a durable HOLD for future fulfillment.
// It cannot publish, change booking rights, send email or grant entitlements.
public sealed class MuaCommerceFoundation(ApplicationDbContext db, TimeProvider clock)
{
    public async Task<MuaCommerceOrder> CreateOrderAsync(Guid owner, string productId, string idempotencyKey, string termsVersion, CancellationToken ct = default, string origin = "IndependentWebsite", string channel = "LocalSimulation")
    {
        if (productId != MuaCommerceCatalog.Visibility) throw new CommerceException("PRODUCT_RETIRED_OR_INVALID");
        var product = MuaCommerceCatalog.Get(productId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 80 || idempotencyKey != idempotencyKey.Trim())
            throw new CommerceException("INVALID_IDEMPOTENCY_KEY");
        if (termsVersion != MuaCommerceCatalog.TermsVersion) throw new CommerceException("TERMS_VERSION_MISMATCH");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(owner, ct);
        var profile = await ActiveProfileAsync(owner, ct);
        var existing = await db.MuaCommerceOrders.SingleOrDefaultAsync(x => x.MuaId == owner && x.IdempotencyKey == idempotencyKey, ct);
        if (existing != null)
        {
            if (existing.ProductId != productId || existing.TermsVersion != termsVersion) throw new CommerceException("IDEMPOTENCY_CONFLICT");
            await tx.CommitAsync(ct); return existing;
        }
        if (profile.VerificationStatus != MuaVerificationStatus.Approved || profile.Status == MuaStatus.Suspended)
            throw new CommerceException("MUA_NOT_APPROVED_OR_SUSPENDED");
        var now = clock.GetUtcNow().UtcDateTime;
        var order = new MuaCommerceOrder { Id = Guid.NewGuid(), MuaId = owner, ProductId = productId, AmountVnd = product.AmountVnd,
            IdempotencyKey = idempotencyKey, TermsVersion = termsVersion, Origin = origin, CreatedAt = now, ExpiresAt = origin == "IndependentWebsite" ? now.AddMinutes(30) : now.AddHours(24), State = MuaCommerceOrderState.PaymentPending };
        db.MuaCommerceOrders.Add(order);
        // Reserve code before any future provider call. The DB unique key is the
        // authority; a caller must retry the whole transaction on a code collision.
        db.MuaCommerceAttempts.Add(new() { Id = Guid.NewGuid(), OrderId = order.Id, MuaId = owner,
            OrderCode = 2000000000000L + RandomNumberGenerator.GetInt32(1, int.MaxValue), Channel = channel, CreatedAt = now });
        Audit(order, "OrderCreated", "", now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return order;
    }

    public async Task BindCreatedLinkAsync(Guid owner, Guid orderId, JsonElement response, string checksumKey, CancellationToken ct = default)
    {
        var data = PayOsCommerceProtocol.VerifiedData(response, checksumKey);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(owner, ct);
        await ActiveProfileAsync(owner, ct);
        var order = await OwnedOrderAsync(owner, orderId, ct);
        var attempt = await db.MuaCommerceAttempts.SingleAsync(x => x.OrderId == orderId && x.MuaId == owner, ct);
        var link = PayOsCommerceProtocol.Text(data, "paymentLinkId");
        if (order.State != MuaCommerceOrderState.PaymentPending || order.ExpiresAt <= clock.GetUtcNow().UtcDateTime
            || PayOsCommerceProtocol.Number(data, "orderCode") != attempt.OrderCode
            || PayOsCommerceProtocol.Number(data, "amount") != order.AmountVnd || PayOsCommerceProtocol.Text(data, "currency") != "VND"
            || PayOsCommerceProtocol.Text(data, "status") != "PENDING" || link.Length > 100
            || attempt.PaymentLinkId != null && attempt.PaymentLinkId != link)
            throw new CommerceException("PAYOS_LINK_MISMATCH");
        attempt.PaymentLinkId = link;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public Task<string> AcceptWebhookAsync(JsonElement envelope, string channel, string checksumKey, CancellationToken ct = default)
    {
        var data = PayOsCommerceProtocol.VerifiedData(envelope, checksumKey);
        if (!envelope.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || PayOsCommerceProtocol.Text(data, "code") != "00" || PayOsCommerceProtocol.Text(data, "currency") != "VND")
            throw new CommerceException("PAYMENT_NOT_SUCCESSFUL_VND");
        return RecordAsync(data, channel, "Webhook", PayOsCommerceProtocol.Text(data, "paymentLinkId"), PayOsCommerceProtocol.Number(data, "amount"),
            PayOsCommerceProtocol.Text(data, "reference"), ct);
    }

    public Task<string> ReconcileAsync(JsonElement envelope, string channel, string checksumKey, CancellationToken ct = default)
    {
        var data = PayOsCommerceProtocol.VerifiedData(envelope, checksumKey);
        if (PayOsCommerceProtocol.Text(data, "status") != "PAID" || PayOsCommerceProtocol.Number(data, "amountRemaining") != 0
            || PayOsCommerceProtocol.Number(data, "amountPaid") != PayOsCommerceProtocol.Number(data, "amount")
            || data.TryGetProperty("currency", out var currency) && currency.GetString() != "VND")
            throw new CommerceException("PAYMENT_NOT_FULLY_PAID");
        var link = PayOsCommerceProtocol.Text(data, "id");
        // GET responses need not include currency or a transaction reference. Price,
        // channel, code, link and a signed PAID total must match the persisted VND order.
        return RecordAsync(data, channel, "Reconciliation", link, PayOsCommerceProtocol.Number(data, "amount"), "reconcile:" + link, ct);
    }

    public async Task<IReadOnlyList<MuaCommerceOrder>> HistoryAsync(Guid owner, CancellationToken ct = default)
    {
        await ActiveProfileAsync(owner, ct);
        return await db.MuaCommerceOrders.AsNoTracking().Where(x => x.MuaId == owner).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
    }

    internal async Task<string> RecordAsync(JsonElement data, string channel, string source, string link, long amount, string reference, CancellationToken ct)
    {
        // Provider identity is an immutable channel mapping in the future adapter,
        // never an unauthenticated client-selected provider name or key.
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 80 || reference.Length > 200)
            throw new CommerceException("INVALID_PROVIDER_IDENTITY");
        var code = PayOsCommerceProtocol.Number(data, "orderCode");
        var found = await db.MuaCommerceAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.OrderCode == code && x.Channel == channel, ct)
            ?? throw new CommerceException("UNKNOWN_PAYMENT");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(found.MuaId, ct);
        var order = await db.MuaCommerceOrders.SingleAsync(x => x.Id == found.OrderId && x.MuaId == found.MuaId, ct);
        await db.Entry(order).ReloadAsync(ct);
        if (found.PaymentLinkId == null || found.PaymentLinkId != link || amount != order.AmountVnd || order.Currency != "VND")
            throw new CommerceException("PAYMENT_MISMATCH");
        var eventKey = PayOsCommerceProtocol.Hash(JsonSerializer.Serialize(new[] { channel, source, reference }));
        var evidence = PayOsCommerceProtocol.Hash(PayOsSignature.Canonicalize(data));
        var receipt = await db.MuaCommerceInbox.AsNoTracking().SingleOrDefaultAsync(x => x.EventKey == eventKey, ct);
        if (receipt != null)
        {
            if (receipt.OrderId != order.Id || source == "Webhook" && receipt.EvidenceHash != evidence)
                throw new CommerceException("EVENT_REPLAY_MISMATCH");
            await tx.CommitAsync(ct); return "Duplicate";
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var outcome = "AlreadyRecorded";
        if (order.VerifiedPaidAt == null)
        {
            // Approval withdrawal produces a manual hold. Existing DB account-deletion
            // guards may reject writes for a deleted owner: the future transport must
            // retain/retry that event in a provider-level durable ingress queue.
            // Never auto-refund, activate or change a booking on these paths.
            var eligible = await db.MakeupArtistProfiles.AsNoTracking().AnyAsync(x => x.MUAId == order.MuaId && x.User != null
                && x.User.IsActive && x.User.DeletedAt == null && !x.User.IsDemoAccount && x.User.Role == UserRole.MUA
                && x.VerificationStatus == MuaVerificationStatus.Approved && x.Status != MuaStatus.Suspended, ct);
            order.VerifiedPaidAt = now;
            order.State = eligible && now < order.ExpiresAt && order.State == MuaCommerceOrderState.PaymentPending
                ? MuaCommerceOrderState.PaidAwaitingFulfillment : MuaCommerceOrderState.ManualReview;
            outcome = order.State.ToString();
            db.MuaCommerceOutbox.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, OrderId = order.Id,
                Kind = "VerifiedPaymentHold", CreatedAt = now, AvailableAt = now });
        }
        db.MuaCommerceInbox.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, OrderId = order.Id,
            EventKey = eventKey, EvidenceHash = evidence, Source = source, Outcome = outcome, ReceivedAt = now });
        Audit(order, outcome, evidence, now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return outcome;
    }

    private async Task<MuaCommerceOrder> OwnedOrderAsync(Guid owner, Guid id, CancellationToken ct) =>
        await db.MuaCommerceOrders.SingleOrDefaultAsync(x => x.Id == id && x.MuaId == owner, ct)
            ?? throw new CommerceException("ORDER_NOT_FOUND");

    private async Task<MakeupArtistProfile> ActiveProfileAsync(Guid owner, CancellationToken ct) =>
        await db.MakeupArtistProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.MUAId == owner && x.User != null
            && x.User.IsActive && x.User.DeletedAt == null && !x.User.IsDemoAccount && x.User.Role == UserRole.MUA, ct)
            ?? throw new CommerceException("MUA_NOT_FOUND");

    private async Task LockOwnerAsync(Guid owner, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await MuaAccessPolicy.Lock(db, owner, ct);
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") throw new CommerceException("UNSUPPORTED_TRANSACTION_PROVIDER");
        // SQLite is used for sequential unit tests only. PostgreSQL tests establish concurrency behavior.
    }

    private void Audit(MuaCommerceOrder order, string action, string evidenceHash, DateTime now) =>
        db.MuaCommerceAudit.Add(new() { Id = Guid.NewGuid(), MuaId = order.MuaId, OrderId = order.Id, Action = action, EvidenceHash = evidenceHash, CreatedAt = now });
}
