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
        public async Task<IActionResult> GetMessages(Guid roomId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var messages = await _chatService.GetMessagesByRoomIdAsync(roomId, userId);
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
                var messageDto = await _chatService.SendMessageAsync(roomId, userId, request.Content);

                // Broadcast via SignalR to the room participants
                // In a real app, we might get the room participants and send to their user groups
                // For simplicity, we just broadcast to the room ID group if they joined it, 
                // OR we send to the specific User IDs.
                // Let's send to the room group. Clients must join the room group when they open the chat.
                await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", messageDto);

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

        [HttpPost("rooms/{roomId}/join")]
        public async Task<IActionResult> JoinRoomGroup(Guid roomId, [FromQuery] string connectionId)
        {
            try
            {
                // Verify user can access room
                var userId = GetCurrentUserId();
                await _chatService.GetMessagesByRoomIdAsync(roomId, userId); // Throws if not allowed
                
                await _hubContext.Groups.AddToGroupAsync(connectionId, roomId.ToString());
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
        
        [HttpPost("rooms/{roomId}/leave")]
        public async Task<IActionResult> LeaveRoomGroup(Guid roomId, [FromQuery] string connectionId)
        {
            try
            {
                await _hubContext.Groups.RemoveFromGroupAsync(connectionId, roomId.ToString());
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }
}
