using System.Security.Claims;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/mua/me/monetization")]
public sealed class MuaMonetizationController(MuaMonetizationReader reader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var result = await reader.GetAsync(userId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }
}
