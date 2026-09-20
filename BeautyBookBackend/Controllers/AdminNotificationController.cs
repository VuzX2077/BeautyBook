using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[Authorize(Roles = "Admin"), ApiController, Route("api/admin/notifications")]
public class AdminNotificationController : ControllerBase
{
    private readonly IAdminNotificationService _service;
    public AdminNotificationController(IAdminNotificationService service) => _service = service;
    private Guid AdminId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Create(CreateAdminNotificationRequest request)
    {
        try { return Ok(await _service.CreateAsync(AdminId, request)); }
        catch (ArgumentException ex) { return BadRequest(new { Code = "INVALID_NOTIFICATION", Message = ex.Message }); }
    }

    [HttpGet]
    public Task<PagedResultDto<AdminNotificationCampaignDto>> GetCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _service.GetCampaignsAsync(page, pageSize);

    [HttpGet("users")]
    public Task<PagedResultDto<AdminNotificationUserDto>> SearchUsers([FromQuery] string? search, [FromQuery] string? role, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.SearchUsersAsync(search, role, page, pageSize);
}
