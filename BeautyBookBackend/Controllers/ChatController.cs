using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs.Chat;
using BeautyBookBackend.Hubs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BeautyBookBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatController(IChatService chatService, IHubContext<ChatHub> hubContext)
        {
            _chatService = chatService;
            _hubContext = hubContext;
        }

        private Guid GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return userId;
        }

        [HttpPost("rooms/{roomId:guid}/images")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("media-upload")]
        [RequestSizeLimit(VerificationImage.MaxBytes + 64 * 1024)]
        public async Task<IActionResult> UploadChatImage(Guid roomId, [FromForm] IFormFile file)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var userId = GetCurrentUserId();
                await _chatService.EnsureRoomAccessAsync(roomId, userId);
                if (file == null || file.Length <= 0 || file.Length > VerificationImage.MaxBytes) return BadRequest(new { Message = "Ảnh không hợp lệ hoặc vượt quá 10 MB." });
                var db = HttpContext.RequestServices.GetRequiredService<BeautyBookBackend.Data.ApplicationDbContext>();
                if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.VerificationMedia.Where(x => x.OwnerId == userId && x.CreatedAt > DateTime.UtcNow.AddHours(-1))) >= 60)
                    return StatusCode(429, new { Message = "Bạn đã tải nhiều ảnh. Vui lòng thử lại sau." });
                using var bytes = new MemoryStream();
                await file.CopyToAsync(bytes, HttpContext.RequestAborted);
                var item = await HttpContext.RequestServices.GetRequiredService<VerificationMediaService>().UploadChatAsync(userId, roomId, bytes.ToArray(), HttpContext.RequestAborted);
                return Ok(new { MediaId = item.Id });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (ArgumentException ex) { return BadRequest(new { Message = ex.Message }); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or DllNotFoundException or TypeInitializationException)
            { return StatusCode(503, new { Message = "Kho ảnh riêng tư đang tạm thời không khả dụng. Vui lòng thử lại." }); }
        }

        [HttpGet("rooms")]
        public async Task<IActionResult> GetChatRooms()
        {
            try
            {
                var userId = GetCurrentUserId();
                var rooms = await _chatService.GetChatRoomsByUserIdAsync(userId);
                return Ok(rooms);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("mua/{muaId}")]
        public async Task<IActionResult> GetOrCreateRoomWithMua(Guid muaId)
        {
            try
            {
                var customerId = GetCurrentUserId();
                var room = await _chatService.GetOrCreateChatRoomAsync(customerId, muaId);
                return Ok(room);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet("rooms/{roomId}/messages")]
        public async Task<IActionResult> GetMessages(Guid roomId, [FromQuery] DateTime? before = null, [FromQuery] int limit = 50)
        {
            try
            {
                var userId = GetCurrentUserId();
                var messages = await _chatService.GetMessagesByRoomIdAsync(roomId, userId, before, limit);
                return Ok(messages);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("rooms/{roomId}/messages")]
        public async Task<IActionResult> SendMessage(Guid roomId, [FromBody] SendMessageRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var messageDto = await _chatService.SendMessageAsync(roomId, userId, request.Content, request.ImageUrl, request.ReplyToMessageId);
                var participants = await _chatService.GetParticipantsAsync(roomId, userId);
                await _hubContext.Clients.Groups(participants.CustomerId.ToString(), participants.MuaId.ToString())
                    .SendAsync("ReceiveMessage", messageDto);

                return Ok(messageDto);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("rooms/{roomId:guid}/messages/{messageId:guid}/reaction")]
        public async Task<IActionResult> ToggleReaction(Guid roomId, Guid messageId, [FromBody] ReactionRequest request)
        {
            try
            {
                var message = await _chatService.ToggleReactionAsync(roomId, messageId, GetCurrentUserId(), request.Emoji);
                var participants = await _chatService.GetParticipantsAsync(roomId, GetCurrentUserId());
                await _hubContext.Clients.Groups(participants.CustomerId.ToString(), participants.MuaId.ToString()).SendAsync("MessageUpdated", message);
                return Ok(message);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        [HttpPost("rooms/{roomId:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid roomId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var count = await _chatService.MarkReadAsync(roomId, userId);
                var participants = await _chatService.GetParticipantsAsync(roomId, userId);
                await _hubContext.Clients.Groups(participants.CustomerId.ToString(), participants.MuaId.ToString())
                    .SendAsync("MessagesRead", new { RoomId = roomId, ReaderId = userId, ReadAt = DateTime.UtcNow });
                return Ok(new { Updated = count });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }
    }
}
