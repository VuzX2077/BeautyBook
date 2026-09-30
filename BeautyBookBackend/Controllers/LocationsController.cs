using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/locations"), EnableRateLimiting("location-lookup")]
public class LocationsController(LocationService locations) : ControllerBase
{
    [HttpGet("areas")]
    public IActionResult Areas() => Ok(OperatingAreas.Catalog);
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { addressSearch = locations.Configured });
    [Authorize, HttpGet("search")]
    public Task<IActionResult> Search([FromQuery, StringLength(200, MinimumLength = 2)] string q, [FromQuery, Required, StringLength(36, MinimumLength = 16)] string sessionToken, CancellationToken ct) => Execute(async () => await locations.SearchAsync(q, sessionToken, ct));
    [Authorize, HttpGet("place")]
    public Task<IActionResult> Select([FromQuery, Required, StringLength(200)] string id, [FromQuery, Required, StringLength(36, MinimumLength = 16)] string sessionToken, CancellationToken ct) => Execute(async () => await locations.SelectAsync(id, sessionToken, ct));
    [Authorize, HttpPost("reverse")]
    public Task<IActionResult> Reverse([FromBody] ReverseRequest request, CancellationToken ct) => Execute(async () => await locations.ReverseAsync(request.Latitude!.Value, request.Longitude!.Value, ct));
    private async Task<IActionResult> Execute(Func<Task<object>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (LocationUnavailableException ex) { return StatusCode(503, new { Code = "LOCATION_UNAVAILABLE", Message = ex.Message }); }
        catch (HttpRequestException) { return StatusCode(503, new { Message = "Dịch vụ địa điểm đang gián đoạn. Vui lòng thử lại." }); }
        catch (TaskCanceledException) { return StatusCode(504, new { Message = "Tra địa điểm quá thời gian chờ." }); }
        catch (JsonException) { return StatusCode(502, new { Message = "Chưa đọc được kết quả địa điểm." }); }
    }
    public sealed class ReverseRequest
    {
        [Required, Range(-90,90)] public double? Latitude { get; set; }
        [Required, Range(-180,180)] public double? Longitude { get; set; }
    }
}
