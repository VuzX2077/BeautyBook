using BeautyBookBackend.Data;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/commerce")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCommerceController(ApplicationDbContext db, MuaEmailCommerce mua, CustomerAiCommerce ai) : ControllerBase
{
    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string kind = "Customer", [FromQuery] int page = 1, [FromQuery] string? state = null, [FromQuery] Guid? userId = null, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000 || kind is not ("Customer" or "MUA")) return BadRequest();
        if (kind == "Customer") {
            var q = db.CustomerAiPurchases.AsNoTracking().Where(x => (state == null || x.State == state) && (!userId.HasValue || x.UserId == userId));
            return Ok(new { total = await q.CountAsync(ct), page, items = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 25).Take(25)
                .Select(x => new { x.Id, userId = x.UserId, x.ProductId, x.ProductName, x.AmountVnd, x.State, x.CreatedAt, paidAt = x.PaidAt }).ToListAsync(ct) });
        }
        var m = db.MuaCommerceOrders.AsNoTracking().Where(x => !userId.HasValue || x.MuaId == userId);
        if (state != null) { if (!Enum.TryParse<Models.MuaCommerceOrderState>(state, out var s)) return BadRequest(); m = m.Where(x => x.State == s); }
        return Ok(new { total = await m.CountAsync(ct), page, items = await m.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 25).Take(25)
            .Select(x => new { x.Id, userId = x.MuaId, x.ProductId, x.ProductName, x.AmountVnd, state = x.State.ToString(), x.CreatedAt, paidAt = x.VerifiedPaidAt }).ToListAsync(ct) });
    }
    [HttpGet("orders/{kind}/{id:guid}")]
    public async Task<IActionResult> Detail(string kind, Guid id, CancellationToken ct)
    {
        if (kind == "Customer") {
            var x = await db.CustomerAiPurchases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (x == null) return NotFound();
            return Ok(new { order = new { x.Id, x.UserId, x.ProductId, x.ProductName, x.PlanVersion, x.AmountVnd, x.State, x.CreatedAt, x.ExpiresAt, x.PaidAt, x.RevokedAt, x.RevocationReason, x.Origin },
                allowedActions = x.PaidAt == null ? Array.Empty<string>() : x.State == "ManualReview" && x.RevokedAt == null ? new[] { "recover", "revoke" } : x.State == "Fulfilled" ? new[] { "revoke" } : Array.Empty<string>(),
                history = await db.MuaCommerceIngress.AsNoTracking().Where(i => i.OrderCode == x.OrderCode).OrderByDescending(i => i.ReceivedAt).Select(i => new { i.Id, i.Source, i.State, i.ReceivedAt, i.Reference }).Take(100).ToListAsync(ct) });
        }
        if (kind != "MUA") return BadRequest();
        var o = await db.MuaCommerceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (o == null) return NotFound();
        return Ok(new { order = new { o.Id, o.MuaId, o.ProductId, o.ProductName, o.PlanVersion, o.DurationDays, o.AmountVnd, state = o.State.ToString(), o.CreatedAt, o.ExpiresAt, o.VerifiedPaidAt, o.Origin },
            allowedActions = o.State == Models.MuaCommerceOrderState.ManualReview && o.VerifiedPaidAt != null ? new[] { "recover" } : Array.Empty<string>(),
            grants = await db.MuaVisibilityGrants.AsNoTracking().Where(g => g.SourceReference == "payos:" + id.ToString("N")).ToListAsync(ct),
            supportHistory = await db.SubscriptionPlanChanges.AsNoTracking().Where(h => h.PlanId == "order:" + id).OrderByDescending(h => h.CreatedAt).Select(h => new { h.Action, h.ActorId, h.Reason, h.CreatedAt }).Take(100).ToListAsync(ct),
            history = await db.MuaCommerceAudit.AsNoTracking().Where(a => a.OrderId == id).OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync(ct) });
    }
    public sealed record Decision(string Action, string EvidenceReference, string Reason);
    [HttpPost("orders/{kind}/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(string kind, Guid id, Decision r, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Unauthorized();
        try {
            if (kind == "Customer") await ai.Resolve(id, actor, r.Action, r.EvidenceReference, r.Reason, ct);
            else if (kind == "MUA" && r.Action == "recover") await mua.Recover(id, actor, ct, r.EvidenceReference, r.Reason);
            else return BadRequest();
            return NoContent();
        } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
}
