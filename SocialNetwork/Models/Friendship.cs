using System;

namespace SocialNetwork.Models
{
    public class Friendship
    {
        public int Id { get; set; }
        public int UserAId { get; set; }   // smaller id for canonical ordering optional
        public int UserBId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
