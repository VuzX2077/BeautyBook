using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers;

// Consumption-only mobile contract: no checkout URLs, payment creation or payment-return claims.
[ApiController, Authorize, Route("api/me/plans")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountPlansController(ApplicationDbContext db, CustomerAiCommerce ai, MuaMonetizationReader mua,
    MuaEmailCommerce commerce, IOptions<MuaMonetizationOptions> flags) : ControllerBase
{
    private bool TryOwner(out Guid owner) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out owner);
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryOwner(out var owner)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct);
        if (user == null) return Forbid();
        var orders = new List<PlanReceipt>();
        if (flags.Value.Enabled)
            orders.AddRange(await db.MuaCommerceOrders.AsNoTracking().Where(x => x.MuaId == owner).OrderByDescending(x => x.CreatedAt)
                .Select(x => new PlanReceipt(x.Id, x.ProductId, x.AmountVnd, x.State.ToString(), x.CreatedAt, x.VerifiedPaidAt)).Take(100).ToListAsync(ct));
        if (ai.CheckoutEnabled || ai.EnforcementEnabled || ai.ReconciliationEnabled)
            orders.AddRange(await db.CustomerAiPurchases.AsNoTracking().Where(x => x.UserId == owner).OrderByDescending(x => x.CreatedAt)
                .Select(x => new PlanReceipt(x.Id, MuaCommerceCatalog.CustomerAi, 20000, x.State, x.CreatedAt, x.PaidAt)).Take(100).ToListAsync(ct));
        var state = user.Role == UserRole.MUA ? await mua.GetAsync(owner, ct) : null;
        return Ok(new { role = user.Role.ToString(), ai = await ai.Status(owner, ct), mua = state,
            orders = orders.OrderByDescending(x => x.CreatedAt).Take(100), evaluatedAt = DateTime.UtcNow });
    }
    [HttpPost("sync"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        if (!TryOwner(out var owner)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct);
        if (user == null) return Forbid();
        if (user.Role == UserRole.Customer) await ai.ReconcilePending(owner, ct);
        else if (user.Role == UserRole.MUA && flags.Value.Enabled && flags.Value.WorkersEnabled && flags.Value.PaymentLinksEnabled)
        {
            var ids = await db.MuaCommerceOrders.AsNoTracking().Where(x => x.MuaId == owner && x.State == BeautyBookBackend.Models.MuaCommerceOrderState.PaymentPending
                && db.MuaCommerceAttempts.Any(a => a.OrderId == x.Id && a.PaymentLinkId != null))
                .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(5).ToListAsync(ct);
            foreach (var id in ids) await commerce.ReconcileOrder(id, ct);
        }
        return await Get(ct);
    }
    public sealed record PlanReceipt(Guid Id, string ProductId, int AmountVnd, string State, DateTime CreatedAt, DateTime? VerifiedPaidAt);
}
