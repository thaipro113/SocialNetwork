using System;
using System.Collections.Generic;

namespace SocialNetwork.Models
{
    public class Conversation
    {
        public int Id { get; set; }
        public int User1Id { get; set; }   // always store the smaller id in User1Id for uniqueness
        public int User2Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User1 { get; set; }   // 🔹 navigation property
        public User? User2 { get; set; }   // 🔹 navigation property

        public List<Message> Messages { get; set; } = new List<Message>();

        // 🆕 Soft Delete flags
        public DateTime? DeletedAtUser1 { get; set; }
        public DateTime? DeletedAtUser2 { get; set; }

        // 🆕 Group Chat Properties
        public bool IsGroup { get; set; } = false;
        public string? GroupName { get; set; }
        public string? GroupAvatar { get; set; }
        public List<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
    }
}
