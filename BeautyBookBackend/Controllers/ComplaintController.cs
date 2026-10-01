using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[Authorize, ApiController, Route("api")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ComplaintController(ComplaintService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool Admin => User.IsInRole("Admin");
    private async Task<IActionResult> Run(Func<Task<object>> action) {
        try { return Ok(await action()); }
        catch (BookingRuleException ex) { return StatusCode(ex.StatusCode, new { ex.Code, Message = ex.Message }); }
    }
    [HttpGet("Booking/{bookingId:guid}/complaint-eligibility")]
    public Task<IActionResult> Eligibility(Guid bookingId) => Run(async () => await service.Eligibility(bookingId, UserId, Admin));
    [HttpGet("Booking/{bookingId:guid}/complaints")]
    public Task<IActionResult> List(Guid bookingId) => Run(() => service.ForBooking(bookingId, UserId, Admin));
    [HttpPost("Booking/{bookingId:guid}/complaints")]
    public Task<IActionResult> Create(Guid bookingId, CreateComplaintRequest request) => Run(async () => new { Id = await service.Create(bookingId, UserId, request) });
    [HttpGet("complaints/{id:guid}")]
    public Task<IActionResult> Detail(Guid id) => Run(() => service.Detail(id, UserId, Admin));
    [HttpPost("complaints/{id:guid}/messages")]
    public Task<IActionResult> Message(Guid id, ComplaintMessageRequest request) => Run(async () => { await service.Message(id, UserId, Admin, request); return await service.Detail(id, UserId, Admin); });
    [Authorize(Roles = "Admin"), HttpGet("admin/complaints")]
    public Task<IActionResult> Queue(string? status = "open", int page = 1, int pageSize = 20) => Run(() => service.AdminList(status, page, pageSize));
    [Authorize(Roles = "Admin"), HttpPost("admin/complaints/{id:guid}/actions")]
    public Task<IActionResult> Action(Guid id, ComplaintActionRequest request) => Run(async () => { await service.Action(id, UserId, request); return await service.Detail(id, UserId, true); });
}
