using System.Security.Claims;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize, Route("api/customer/me/ai-access")]
public sealed class CustomerAiAccessController(CustomerAiCommerce commerce) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner)) return Unauthorized();
        return Ok(await commerce.Status(owner, ct));
    }
}
