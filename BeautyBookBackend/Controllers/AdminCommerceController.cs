using BeautyBookBackend.Data;
using BeautyBookBackend.Services.Monetization;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/commerce")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCommerceController(ApplicationDbContext db, MuaEmailCommerce mua, CustomerAiCommerce ai, IOptions<MuaMonetizationOptions> options) : ControllerBase
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
            allowedActions = o.VerifiedPaidAt == null ? Array.Empty<string>() : o.State == Models.MuaCommerceOrderState.ManualReview ? new[] { "recover" }
                : o.State == Models.MuaCommerceOrderState.PaidAwaitingFulfillment && options.Value.WorkersEnabled ? new[] { "retry" } : Array.Empty<string>(),
            grants = await db.MuaVisibilityGrants.AsNoTracking().Where(g => g.SourceReference == "payos:" + id.ToString("N")).ToListAsync(ct),
            fulfillment = new { workersEnabled = options.Value.WorkersEnabled, sponsoredDeliveryEnabled = options.Value.SponsoredDeliveryEnabled,
                lastJob = await db.MuaCommerceJobs.AsNoTracking().Where(j => j.TargetId == id && j.Kind == "Fulfill").OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
                    .Select(j => new { j.State, j.Attempts, j.LastError, j.AvailableAt }).FirstOrDefaultAsync(ct) },
            promotionTasks = await db.MuaFacebookTasks.AsNoTracking().Where(t => t.OrderId == id).OrderBy(t => t.CycleStartsAt)
                .Select(t => new { t.Id, t.State, t.CycleStartsAt, t.DueAt, t.PublishedUrl, t.PublishedAt }).ToListAsync(ct),
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
            else if (kind == "MUA" && r.Action == "retry") await mua.RetryFulfillment(id, actor, r.EvidenceReference, r.Reason, ct);
            else return BadRequest();
            return NoContent();
        } catch (CommerceException ex) { return Conflict(new { code = ex.Code, message = ex.Code switch {
            "COMMERCE_WORKER_DISABLED" => "Xử lý kích hoạt tự động đang tắt trên backend.",
            "MUA_NOT_ELIGIBLE" => "Hồ sơ MUA chưa đủ điều kiện. Kiểm tra xác minh, trạng thái tài khoản và các thông tin bắt buộc.",
            "NO_VERIFIED_PAYMENT_TO_RECOVER" => "Đơn chưa có khoản tiền được xác minh để xử lý lại.",
            "ORDER_NOT_AWAITING_FULFILLMENT" => "Trạng thái đơn đã thay đổi. Hãy làm mới để xem thao tác phù hợp.",
            "INVALID_SUPPORT_DECISION" => "Nhập bằng chứng đối soát và lý do hợp lệ.",
            _ => "Chưa xử lý được đơn. Mã kiểm tra: " + ex.Code } }); }
    }
}
