using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocialNetwork.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; } // Chủ bài viết (người nhận thông báo)
        public int FromUserId { get; set; } // Người tạo thông báo (người like/comment)
        public int? PostId { get; set; }
        public int? FriendRequestId { get; set; } // ID của FriendRequest (nếu là thông báo friend)
        public string Type { get; set; } // "like", "comment", "friend_request", "friend_accepted"
        public string Message { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // 👇 Thêm navigation properties
        [ForeignKey("PostId")]
        public virtual Post Post { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [ForeignKey("FromUserId")]
        public virtual User FromUser { get; set; }
    }

}
