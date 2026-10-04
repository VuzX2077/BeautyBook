using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[Authorize, ApiController, Route("api"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ModerationController(ModerationService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (BookingRuleException ex) { return StatusCode(ex.StatusCode, new { ex.Code, ex.Message }); }
    }
    [HttpGet("moderation/blocks")] public Task<IActionResult> Blocks() => Run(() => service.Blocks(Actor));
    [HttpPut("moderation/blocks/{id:guid}")] public Task<IActionResult> Block(Guid id) => Run(async () => { await service.Block(Actor, id, true); return new { Blocked = true }; });
    [HttpDelete("moderation/blocks/{id:guid}")] public Task<IActionResult> Unblock(Guid id) => Run(async () => { await service.Block(Actor, id, false); return new { Blocked = false }; });
    [HttpPost("moderation/reports")] public Task<IActionResult> Report(CreateContentReportRequest request) => Run(async () => new { Id = await service.Report(Actor, request) });
    [Authorize(Roles = "Admin"), HttpGet("admin/moderation/reports")] public Task<IActionResult> Queue(string? status = "Pending", int page = 1) => Run(() => service.Queue(Actor, status, page));
    [Authorize(Roles = "Admin"), HttpGet("admin/moderation/reports/{id:guid}")] public Task<IActionResult> Detail(Guid id) => Run(() => service.Detail(Actor, id));
    [Authorize(Roles = "Admin"), HttpGet("admin/moderation/reports/{id:guid}/image")] public Task<IActionResult> Image(Guid id, [FromServices] IVerificationStorage storage) => Run(() => service.ReportedImage(Actor, id, storage));
    [Authorize(Roles = "Admin"), HttpPost("admin/moderation/reports/{id:guid}/decision")] public Task<IActionResult> Decision(Guid id, ModerationDecisionRequest request) => Run(async () => { await service.Decide(Actor, id, request); return await service.Detail(Actor, id); });
}
