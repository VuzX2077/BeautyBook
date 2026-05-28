using System;

namespace BeautyBookBackend.Models
{
    public class ChatRoom
    {
        public Guid ChatRoomId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid MUAId { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User? Customer { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
