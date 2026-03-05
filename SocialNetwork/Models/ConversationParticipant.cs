using System;

namespace SocialNetwork.Models
{
    public class ConversationParticipant
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Role { get; set; } = "Member"; // "Admin", "Member"
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeletedAt { get; set; }
    }
}
