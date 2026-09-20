using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[Authorize, ApiController, Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly IBookingNotificationService _service;
    public NotificationController(IBookingNotificationService service) => _service = service;
    private bool TryGetCurrentUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    [HttpPost("device-token")]
    public async Task<IActionResult> Register([FromBody] RegisterPushTokenRequest request)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!IsExpoPushToken(request.ExpoPushToken)) return BadRequest("Expo push token is invalid.");
        if (!string.Equals(request.Platform, "android", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Platform, "ios", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Platform must be android or ios.");

        await _service.RegisterDeviceAsync(userId, request.ExpoPushToken, request.Platform.ToLowerInvariant(), request.DeviceName);
        return Ok();
    }

    [HttpDelete("device-token")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterPushTokenRequest request)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!IsExpoPushToken(request.ExpoPushToken)) return BadRequest("Expo push token is invalid.");

        await _service.UnregisterDeviceAsync(userId, request.ExpoPushToken);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> GetInbox([FromQuery] int take = 50)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return Ok(await _service.GetInboxAsync(userId, take));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return Ok(new { Count = await _service.GetUnreadCountAsync(userId) });
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return await _service.MarkReadAsync(userId, id) ? NoContent() : NotFound();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        await _service.MarkAllReadAsync(userId);
        return NoContent();
    }

    private static bool IsExpoPushToken(string token) =>
        !string.IsNullOrWhiteSpace(token)
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
            || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']');
}
