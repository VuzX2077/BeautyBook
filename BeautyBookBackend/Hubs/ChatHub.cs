using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using BeautyBookBackend.Services;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private readonly ApplicationDbContext _db;
        private readonly AccountConnections _connections;
        public ChatHub(IChatService chatService, ApplicationDbContext db, AccountConnections connections)
        { _chatService = chatService; _db = db; _connections = connections; }

        private async Task RequireActiveAsync()
        {
            var id = CurrentUserId();
            if (!await _db.Users.AnyAsync(x => x.UserId == id && x.IsActive && x.DeletedAt == null)) {
                Context.Abort(); throw new HubException("Tài khoản không còn hoạt động.");
            }
        }

        private Guid CurrentUserId()
        {
            var value = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(value, out var id) ? id : throw new HubException("Token không hợp lệ.");
        }

        public override async Task OnConnectedAsync()
        {
            // Register before checking DB: deletion between those steps still aborts us.
            _connections.Register(CurrentUserId(), Context);
            await RequireActiveAsync();
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                // Join a group with the user's ID to receive private messages
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            }
            await base.OnConnectedAsync();
        }

        public async Task JoinRoom(Guid roomId)
        {
            await RequireActiveAsync();
            await _chatService.EnsureRoomAccessAsync(roomId, CurrentUserId());
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId.ToString());
        }

        public Task LeaveRoom(Guid roomId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId.ToString());

        public async Task Typing(Guid roomId, bool isTyping)
        {
            await RequireActiveAsync();
            var userId = CurrentUserId();
            await _chatService.EnsureRoomAccessAsync(roomId, userId);
            await Clients.OthersInGroup(roomId.ToString()).SendAsync("TypingChanged", new { RoomId = roomId, UserId = userId, IsTyping = isTyping });
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connections.Remove(CurrentUserId(), Context.ConnectionId);
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
            }
            await base.OnDisconnectedAsync(exception);
        }
        
        // Clients will call API via REST to save message, then API will call HubContext to broadcast
        // Alternatively, they can send directly through Hub, but REST + Hub Context is often cleaner for error handling
    }
}
