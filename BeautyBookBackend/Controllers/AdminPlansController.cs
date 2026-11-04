using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/plans")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPlansController(ApplicationDbContext db, TimeProvider clock) : ControllerBase
{
    public sealed record Edit(string Id, string Role, string Name, int AmountVnd, int? Days, string Description, string State, int Version, string Reason);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.SubscriptionPlans.AsNoTracking().OrderBy(x => x.Role).ThenBy(x => x.Name).ToListAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var plan = await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        var history = db.SubscriptionPlanChanges.AsNoTracking().Where(x => x.PlanId == id);
        if (plan == null && !await history.AnyAsync(ct)) return NotFound();
        return Ok(new { plan, history = await history.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 25).Take(25).ToListAsync(ct), total = await history.CountAsync(ct), page });
    }

    [HttpPost]
    public Task<IActionResult> Create(Edit request, CancellationToken ct) => Save(request.Id, request, true, ct);
    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, Edit request, CancellationToken ct) => Save(id, request, false, ct);

    private async Task<IActionResult> Save(string id, Edit r, bool create, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || !await db.Users.AnyAsync(x => x.UserId == actor && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct)) return Forbid();
        if (id != r.Id || !Regex.IsMatch(id, "^[a-z0-9_]{3,64}$") || r.Role is not ("Customer" or "MUA") || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 120
            || r.AmountVnd < 1000 || r.AmountVnd > 100000000 || r.Description == null || r.Description.Length > 1000 || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 1000
            || r.State is not ("Draft" or "Published" or "Paused" or "Archived") || (r.Role == "Customer" ? r.Days != null : (r.Days == null || r.Days < 30 || r.Days > 360 || r.Days % 30 != 0))) return BadRequest(new { code = "INVALID_PLAN" });
        // MUA packages consist of whole 30-day delivery cycles.
        var p = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (create && (p != null || await db.SubscriptionPlanChanges.AnyAsync(x => x.PlanId == id, ct))) return Conflict(new { code = "PLAN_ID_EXISTS" });
        if (!create && p == null) return NotFound();
        if (p != null && (p.Version != r.Version || p.Role != r.Role)) return Conflict(new { code = "PLAN_VERSION_CONFLICT" });
        var before = p == null ? "null" : JsonSerializer.Serialize(p);
        p ??= new() { Id = id, Role = r.Role };
        if (create) db.SubscriptionPlans.Add(p); else p.Version++;
        p.Name = r.Name.Trim(); p.AmountVnd = r.AmountVnd; p.Days = r.Days; p.Description = r.Description.Trim(); p.State = r.State;
        db.SubscriptionPlanChanges.Add(new() { Id = Guid.NewGuid(), PlanId = id, ActorId = actor, Action = create ? "Created" : "Updated", Reason = r.Reason.Trim(), BeforeJson = before, AfterJson = JsonSerializer.Serialize(p), CreatedAt = clock.GetUtcNow().UtcDateTime });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "PLAN_VERSION_CONFLICT" }); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Conflict(new { code = "PLAN_ID_EXISTS" }); }
        return Ok(p);
    }

    public sealed record DeleteRequest(int Version, string Reason);
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, DeleteRequest r, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || !await db.Users.AnyAsync(x => x.UserId == actor && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 1000) return BadRequest();
        var p = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.Id == id, ct); if (p == null) return NotFound();
        if (p.Version != r.Version) return Conflict(new { code = "PLAN_VERSION_CONFLICT" });
        // Archive preserves identity and remains safe against in-flight checkout transactions.
        var before = JsonSerializer.Serialize(p); p.State = "Archived"; p.Version++;
        db.SubscriptionPlanChanges.Add(new() { Id = Guid.NewGuid(), PlanId = id, ActorId = actor, Action = "Archived", Reason = r.Reason.Trim(), BeforeJson = before, AfterJson = JsonSerializer.Serialize(p), CreatedAt = clock.GetUtcNow().UtcDateTime });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(new { code = "PLAN_VERSION_CONFLICT" }); }
        return NoContent();
    }
}
