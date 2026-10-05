using BeautyBookBackend.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace BeautyBookBackend.Controllers;
[ApiController, Route("api/locations"), EnableRateLimiting("location-lookup")]
public class LocationsController : ControllerBase
{
    [HttpGet("areas")]
    public IActionResult Areas() => Ok(OperatingAreas.Catalog);
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { addressSearch = false });
    // Authenticated legacy tombstones; no external provider request.
    [Authorize, HttpGet("search")]
    public IActionResult Search() => Unavailable();
    [Authorize, HttpGet("place")]
    public IActionResult Select() => Unavailable();
    [Authorize, HttpPost("reverse")]
    public IActionResult Reverse() => Unavailable();
    private IActionResult Unavailable()
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(410, new { Code = "LOCATION_LOOKUP_RETIRED", Message = "Tra cứu địa chỉ không còn được hỗ trợ. Hãy nhập địa chỉ thủ công; GPS là không bắt buộc." });
    }
}
