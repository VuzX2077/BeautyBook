using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class CustomerAiCommerce
{
    // Support records an independently verified refund/recovery decision. No booking money is moved.
    public async Task Resolve(Guid id, Guid admin, string action, string evidence, string reason, CancellationToken ct)
    {
        if (action is not ("recover" or "revoke") || string.IsNullOrWhiteSpace(evidence) || evidence.Length > 120 || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new CommerceException("INVALID_SUPPORT_DECISION");
        if (!await db.Users.AnyAsync(x => x.UserId == admin && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct)) throw new CommerceException("ADMIN_REQUIRED");
        var row = await db.CustomerAiPurchases.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CommerceException("ORDER_NOT_FOUND");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(row.UserId, ct); await db.Entry(row).ReloadAsync(ct);
        var key = PayOsCommerceProtocol.Hash($"customer-ai-support:{id}:{action}:{evidence}");
        var proof = PayOsCommerceProtocol.Hash(JsonSerializer.Serialize(new { id, admin, action, evidence, reason }));
        var prior = await db.MuaCommerceIngress.SingleOrDefaultAsync(x => x.EventKey == key, ct);
        if (prior != null) { if (prior.EvidenceHash != proof) throw new CommerceException("SUPPORT_DECISION_CONFLICT"); await tx.CommitAsync(ct); return; }
        if (row.PaidAt == null) throw new CommerceException("NO_VERIFIED_PAYMENT_TO_RECOVER");
        await Customer(row.UserId, ct);
        if (action == "recover")
        {
            if (row.State != "ManualReview" || row.RevokedAt != null) throw new CommerceException("NO_PAYMENT_TO_RECOVER");
            if (await db.CustomerAiPurchases.AnyAsync(x => x.UserId == row.UserId && x.Id != id && x.State == "Fulfilled" && x.RevokedAt == null, ct)) throw new CommerceException("AI_ALREADY_PURCHASED");
            row.State = "Fulfilled";
        }
        else { row.RevokedAt ??= Now; row.RevocationReason = reason; row.State = "Revoked"; }
        db.MuaCommerceIngress.Add(new() { Id = Guid.NewGuid(), EventKey = key, EvidenceHash = proof, OrderCode = row.OrderCode, AmountVnd = 20000,
            PaymentLinkId = row.PaymentLinkId ?? "", Reference = $"admin:{admin:N}:{evidence}", Source = "CustomerAiSupport", State = "Recorded", ReceivedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
