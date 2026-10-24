using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = nameof(UserRole.Admin)), Route("api/admin/makeup-catalog/terms")]
public sealed class AdminMakeupCatalogController(MakeupCatalogService service, ApplicationDbContext db) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    [HttpGet]
    public async Task<IActionResult> List(string? kind = null, string? search = null, string status = "all", int? groupId = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => Ok(await service.List(kind, search, status, groupId, page, pageSize, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct) => await db.MakeupCatalogTerms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } t ? Ok(MakeupCatalogService.Map(t)) : NotFound();
    [HttpPost]
    public async Task<IActionResult> Create(CatalogTermWriteRequest request) { var term = await service.SaveTerm(null, request, Actor); return CreatedAtAction(nameof(Detail), new { id = term.Id }, term); }
    [HttpPut("{id:int:min(1)}")]
    public async Task<IActionResult> Update(int id, CatalogTermWriteRequest request) => Ok(await service.SaveTerm(id, request, Actor));
    [HttpPatch("{id:int:min(1)}/status")]
    public async Task<IActionResult> Status(int id, CatalogLifecycleRequest request) => Ok(await service.SetTermStatus(id, request, Actor));
    [HttpPost("{id:int:min(1)}/archive")]
    public async Task<IActionResult> Archive(int id, CatalogLifecycleRequest request) => Ok(await service.SetTermStatus(id, request, Actor, true));
    [HttpGet("{id:int:min(1)}/usage")]
    public async Task<IActionResult> Usage(int id, CancellationToken ct) => await db.MakeupCatalogTerms.AnyAsync(x => x.Id == id, ct) ? Ok(await service.TermUsage(id, ct)) : NotFound();
}
