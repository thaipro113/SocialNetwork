using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocialNetwork.Models
{
    public class Story
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public string MediaUrl { get; set; } // URL của ảnh/video
        public string Type { get; set; } // "Image" hoặc "Video"
        public string Content { get; set; } // Caption (tùy chọn)

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ExpiresAt { get; set; } = DateTime.Now.AddHours(24);

        public bool IsExpired => DateTime.Now > ExpiresAt;
    }
}
