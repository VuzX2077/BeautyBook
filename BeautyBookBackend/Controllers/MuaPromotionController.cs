using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
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

[ApiController, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MuaPromotionController(ApplicationDbContext db, IOptions<MuaMonetizationOptions> options, TimeProvider clock) : ControllerBase
{
    [AllowAnonymous, HttpGet("api/Mua/sponsored"), EnableRateLimiting("explore")]
    public async Task<IActionResult> Sponsored([FromQuery] ExploreQuery request, CancellationToken ct)
    {
        if (!options.Value.SponsoredDeliveryEnabled || request.AdvisorStyleKey != null) return Ok(new { items = Array.Empty<object>() });
        var now = clock.GetUtcNow().UtcDateTime;
        var query = MuaAccessPolicy.Public(db, db.MakeupArtistProfiles).AsNoTracking().Where(m => m.Status == MuaStatus.Listed
            && m.VerificationStatus == MuaVerificationStatus.Approved && m.User != null && m.User.IsActive && m.User.DeletedAt == null && !m.User.IsDemoAccount
            && db.MuaVisibilityGrants.Any(g => g.MuaId == m.MUAId && g.RevokedAt == null && g.StartsAt <= now && now < g.EndsAt));
        if (request.ProvinceCode.HasValue) query = query.Where(m => m.OperatingProvinceCode == request.ProvinceCode || m.ProvinceCode == request.ProvinceCode);
        if (request.StyleId.HasValue) query = query.Where(m => db.MUAStyles.Any(s => s.MUAId == m.MUAId && s.StyleId == request.StyleId));
        if (request.MinPrice.HasValue || request.MaxPrice.HasValue) query = query.Where(m => m.Services.Any(s => s.IsActive && (!request.MinPrice.HasValue || s.Price >= request.MinPrice) && (!request.MaxPrice.HasValue || s.Price <= request.MaxPrice)));
        if (!string.IsNullOrWhiteSpace(request.Q)) { var q = request.Q.Trim().ToLower(); query = query.Where(m => m.User!.FullName != null && m.User.FullName.ToLower().Contains(q) || m.Services.Any(s => s.IsActive && s.ServiceName.ToLower().Contains(q))); }
        var viewer = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        if (viewer.HasValue) query = query.Where(m => !db.UserBlocks.Any(b => b.BlockerId == viewer.Value && b.BlockedId == m.MUAId || b.BlockerId == m.MUAId && b.BlockedId == viewer.Value));
        var count = await query.CountAsync(ct); if (count == 0) return Ok(new { items = Array.Empty<object>() });
        var offset = (int)((clock.GetUtcNow().ToUnixTimeSeconds() / 300 * 6) % count);
        var projection = query.OrderBy(x => x.MUAId).Select(x => new { id = x.MUAId, name = x.User!.FullName, x.City, x.User.AvatarUrl, rating = x.AverageRating, label = "Quảng bá" });
        var items = await projection.Skip(offset).Take(6).ToListAsync(ct);
        if (items.Count < Math.Min(count, 6)) items.AddRange(await projection.Take(Math.Min(count, 6) - items.Count).ToListAsync(ct));
        return Ok(new { items });
    }
    public sealed record SponsoredEvent(Guid Id, Guid MuaId, string Kind);
    [AllowAnonymous, HttpPost("api/Mua/sponsored/events"), EnableRateLimiting("explore")]
    public async Task<IActionResult> Events(SponsoredEvent request, CancellationToken ct)
    {
        if (!options.Value.SponsoredDeliveryEnabled) return NoContent();
        if (request.Id == Guid.Empty || request.Kind is not ("Impression" or "ProfileView")) return BadRequest();
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await MuaAccessPolicy.Public(db, db.MakeupArtistProfiles).AnyAsync(m => m.MUAId == request.MuaId && m.Status == MuaStatus.Listed && m.User != null && m.User.IsActive && m.User.DeletedAt == null
            && db.MuaVisibilityGrants.Any(g => g.MuaId == m.MUAId && g.RevokedAt == null && g.StartsAt <= now && now < g.EndsAt), ct)) return NoContent();
        if (await db.MuaSponsoredEvents.AnyAsync(x => x.Id == request.Id, ct)) return NoContent();
        db.MuaSponsoredEvents.Add(new() { Id = request.Id, MuaId = request.MuaId, Kind = request.Kind, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }) { }
        return NoContent();
    }
    [Authorize(Roles = "Admin"), HttpGet("api/admin/mua-promotions")]
    public async Task<IActionResult> Tasks([FromQuery] string? state, CancellationToken ct)
    {
        if (!options.Value.WebPortalEnabled) return Ok(Array.Empty<object>());
        var tasks = db.MuaFacebookTasks.AsNoTracking(); if (!string.IsNullOrWhiteSpace(state)) tasks = tasks.Where(x => x.State == state);
        return Ok(await tasks.OrderBy(x => x.DueAt).Select(x => new { x.Id, x.MuaId, name = db.Users.Where(u => u.UserId == x.MuaId).Select(u => u.FullName).FirstOrDefault(),
            x.State, x.CycleStartsAt, x.DueAt, x.Draft, x.PublishedUrl, x.PublishedAt, x.Revisions, x.RevisionRequest, x.RemedyNote }).Take(100).ToListAsync(ct));
    }
    [Authorize(Roles = "Admin"), HttpGet("api/admin/mua-promotions/{id:guid}/portfolio")]
    public async Task<IActionResult> Sources(Guid id, CancellationToken ct)
    {
        var task = await db.MuaFacebookTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (task == null) return NotFound();
        var images = await db.Portfolios.AsNoTracking().Where(x => x.MUAId == task.MuaId && !x.IsHidden && !db.ContentReports.Any(r => r.TargetId == x.PortfolioId && r.TargetType == "Portfolio" && r.Status == "Removed"))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Title, x.ImageUrls }).Take(20).ToListAsync(ct);
        return Ok(images.Select(x => new { x.Title, imageUrls = x.ImageUrls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https").Take(10) }));
    }
    public sealed record PromotionUpdate(string State, string Draft, string? PublishedUrl, string? RemedyNote);
    [Authorize(Roles = "Admin"), HttpPut("api/admin/mua-promotions/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PromotionUpdate request, CancellationToken ct)
    {
        var admin = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); await new PlayReviewPolicy(db).EnsureNormalUserAsync(admin);
        if (request.State is not ("Preparing" or "Scheduled" or "Published" or "NeedsContent") || request.Draft.Length > 6000 || request.RemedyNote?.Length > 1000) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var task = await db.MuaFacebookTasks.SingleOrDefaultAsync(x => x.Id == id, ct); if (task == null) return NotFound();
        await MuaAccessPolicy.Lock(db, task.MuaId, ct); await db.Entry(task).ReloadAsync(ct);
        if (request.State == "Published")
        {
            if (clock.GetUtcNow().UtcDateTime < task.CycleStartsAt || !Uri.TryCreate(request.PublishedUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0
                || uri.Host is not ("facebook.com" or "www.facebook.com" or "m.facebook.com") || uri.AbsolutePath == "/") return BadRequest();
            if (task.PublishedAt == null && clock.GetUtcNow().UtcDateTime > task.DueAt && string.IsNullOrWhiteSpace(request.RemedyNote)) return Conflict(new { code = "LATE_DELIVERY_REMEDY_REQUIRED" });
            task.PublishedAt ??= clock.GetUtcNow().UtcDateTime; task.PublishedUrl = request.PublishedUrl;
        }
        else if (task.PublishedAt != null && task.State != "RevisionRequested") return Conflict(new { code = "PUBLICATION_ALREADY_RECORDED" });
        task.State = request.State; task.Draft = request.Draft; task.RemedyNote = request.RemedyNote;
        db.MuaCommerceAudit.Add(new() { Id = Guid.NewGuid(), MuaId = task.MuaId, OrderId = task.OrderId, Action = "FacebookUpdated", EvidenceHash = PayOsCommerceProtocol.Hash(System.Text.Json.JsonSerializer.Serialize(new { admin, request })), CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
}
