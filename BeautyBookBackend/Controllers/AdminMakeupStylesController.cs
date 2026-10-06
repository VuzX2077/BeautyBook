using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/admin/makeup-styles")]
public sealed class AdminMakeupStylesController(AdminMakeupStyleService service) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    [HttpGet]
    public async Task<IActionResult> List(int page = 1, int pageSize = 10, string? search = null, string status = "all", CancellationToken ct = default)
    {
        try { return Ok(await service.List(page, pageSize, search, status, ct)); }
        catch (ArgumentException e) { return BadRequest(new { Message = e.Message }); }
    }
    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct = default) =>
        await service.Detail(id, ct) is { } style ? Ok(style) : NotFound(new { Message = "Không tìm thấy phong cách." });
    [HttpPost]
    public Task<IActionResult> Create(AdminMakeupStyleWriteRequest request, CancellationToken ct = default) => Write(null, request, ct);
    [HttpPut("{id:int:min(1)}")]
    public Task<IActionResult> Update(int id, AdminMakeupStyleWriteRequest request, CancellationToken ct = default) => Write(id, request, ct);
    private async Task<IActionResult> Write(int? id, AdminMakeupStyleWriteRequest request, CancellationToken ct)
    {
        if (Actor == Guid.Empty) return Forbid();
        try
        {
            var result = await service.Save(id, request, Actor, ct);
            if (result == null) return NotFound(new { Message = "Không tìm thấy phong cách." });
            return id.HasValue ? Ok(result) : CreatedAtAction(nameof(Detail), new { id = result.StyleId }, result);
        }
        catch (ArgumentException e) { return BadRequest(new { Message = e.Message }); }
        catch (MakeupStyleConflictException e) { return Conflict(new { Message = e.Message }); }
        catch (PlayReviewOperationException) { return Forbid(); }
    }
    [HttpPatch("{id:int:min(1)}/status")]
    public async Task<IActionResult> Status(int id, AdminMakeupStyleStatusRequest request, CancellationToken ct = default)
    {
        if (Actor == Guid.Empty) return Forbid();
        if (!request.IsActive.HasValue) return BadRequest(new { Message = "Chọn trạng thái phong cách." });
        try { return await service.SetStatus(id, request.IsActive.Value, Actor, ct) is { } style ? Ok(style) : NotFound(new { Message = "Không tìm thấy phong cách." }); }
        catch (PlayReviewOperationException) { return Forbid(); }
    }
}
