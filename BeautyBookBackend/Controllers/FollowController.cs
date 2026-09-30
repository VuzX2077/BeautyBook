using System.Security.Claims;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BeautyBookBackend.Controllers;
[ApiController, Route("api/Follow")]
public class FollowController(FollowService service) : ControllerBase
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    [HttpGet("{muaId:guid}")]
    public async Task<IActionResult> Status(Guid muaId) => await service.GetStatus(muaId, UserId) is { } status ? Ok(status) : NotFound();
    [Authorize, HttpPut("{muaId:guid}")]
    public Task<IActionResult> Follow(Guid muaId) => Set(muaId, true);
    [Authorize, HttpDelete("{muaId:guid}")]
    public Task<IActionResult> Unfollow(Guid muaId) => Set(muaId, false);
    private async Task<IActionResult> Set(Guid muaId, bool following)
    {
        if (UserId is not { } userId) return Unauthorized();
        try { return await service.SetFollowing(muaId, userId, following) is { } status ? Ok(status) : NotFound(); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
    [Authorize, HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        if (UserId is not { } userId) return Unauthorized();
        if (page < 1 || page > 100000 || limit < 1 || limit > 50) return BadRequest();
        return Ok(await service.GetFollowing(userId, page, limit));
    }
}
