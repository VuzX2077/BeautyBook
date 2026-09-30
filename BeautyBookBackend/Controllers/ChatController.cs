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
