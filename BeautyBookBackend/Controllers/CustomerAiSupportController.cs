using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/customer-ai/orders")]
public sealed class CustomerAiSupportController(ApplicationDbContext db, CustomerAiCommerce commerce) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!commerce.CheckoutEnabled && !commerce.EnforcementEnabled && !commerce.ReconciliationEnabled) return Ok(new { orders = Array.Empty<object>(), notifications = Array.Empty<object>() });
        var orders = await db.CustomerAiPurchases.AsNoTracking().Where(x => x.State == "ManualReview" || x.State == "Revoked")
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.UserId, x.State, x.CreatedAt, x.PaidAt, x.RevokedAt, x.RevocationReason }).Take(100).ToListAsync(ct);
        var notifications = await db.MuaCommerceIngress.AsNoTracking().Where(x => x.Source == "CustomerAiWebhook" && x.State == "ManualReview")
            .OrderByDescending(x => x.ReceivedAt).Select(x => new { x.Id, x.OrderCode, x.AmountVnd, x.Reference, x.State, x.ReceivedAt }).Take(100).ToListAsync(ct);
        return Ok(new { orders, notifications });
    }
    public sealed record Decision(string Action, string EvidenceReference, string Reason);
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, Decision request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var admin)) return Unauthorized();
        try { await commerce.Resolve(id, admin, request.Action, request.EvidenceReference, request.Reason, ct); return NoContent(); }
        catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
}
