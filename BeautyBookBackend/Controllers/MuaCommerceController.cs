using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/mua/commerce")]
public sealed class MuaCommerceController(MuaEmailCommerce commerce, CustomerAiCommerce customerAi, IConfiguration config) : ControllerBase
{
    [AllowAnonymous, HttpPost("payos/webhook"), RequestSizeLimit(32768)]
    public async Task<IActionResult> Webhook(JsonElement envelope, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(new { status = await customerAi.Receive(envelope, ct) ? "Accepted" : await commerce.Receive(envelope, ct) }); }
        catch (Exception ex) when (ex is CommerceException or JsonException or InvalidOperationException or FormatException)
        { return BadRequest(new { code = "INVALID_PAYMENT_NOTIFICATION" }); }
        // DB/network/transient failures deliberately produce non-2xx. payOS can retry.
    }
    public sealed record OfferRequest(Guid MuaId, string EvidenceReference, string Kind = "Requested");
    [Authorize(Roles = "Admin"), HttpPost("offers")]
    public async Task<IActionResult> Offer(OfferRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var admin)) return Unauthorized();
        try { return Ok(new { id = await commerce.Offer(admin, request.MuaId, request.EvidenceReference, request.Kind, ct) }); }
        catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [Authorize(Roles = "Admin"), HttpPost("orders/{id:guid}/recover")]
    public async Task<IActionResult> Recover(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var admin)) return Unauthorized();
        try { await commerce.Recover(id, admin, ct); return NoContent(); } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [AllowAnonymous, HttpGet("result")]
    public IActionResult Result() => Content("<!doctype html><html lang=vi><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>BBook</title><h1>BBook</h1><p>Kết quả thanh toán được xác minh tự động. Bạn có thể mở lại BBook để đồng bộ trạng thái. Trang này không xác nhận giao dịch thành công.</p></html>", "text/html; charset=utf-8");
    [AllowAnonymous, HttpGet("unsubscribe")]
    public IActionResult UnsubscribePage([FromQuery] string token) => Content("<!doctype html><html lang=vi><meta charset=utf-8><title>BBook</title><form method=post><input type=hidden name=token value=\"" + System.Net.WebUtility.HtmlEncode(token) + "\"><button>Xác nhận từ chối đề nghị qua email</button></form></html>", "text/html; charset=utf-8");
    [AllowAnonymous, HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromForm] string token, CancellationToken ct)
    { try { await commerce.Unsubscribe(token, ct); return Content("Đã ghi nhận từ chối đề nghị này.", "text/plain; charset=utf-8"); } catch (CommerceException) { return BadRequest(); } }
    [AllowAnonymous, HttpPost("email-events"), RequestSizeLimit(16384)]
    public async Task<IActionResult> EmailEvents(JsonElement body, CancellationToken ct)
    {
        var key = config["CommerceEmail:WebhookBearerToken"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes("Bearer " + key), Encoding.UTF8.GetBytes(Request.Headers.Authorization.ToString()))) return Unauthorized();
        try
        {
            string? tag = body.TryGetProperty("tag", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            await commerce.Delivery(PayOsCommerceProtocol.Text(body, "message-id"), PayOsCommerceProtocol.Text(body, "email"), PayOsCommerceProtocol.Text(body, "event"), tag, ct);
            return Ok();
        }
        catch (CommerceException) { return BadRequest(); }
    }
}
