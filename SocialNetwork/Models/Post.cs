using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocialNetwork.Models;

[Table("Post")]
public partial class Post
{
    [Key]
    public int PostId { get; set; }

    [Required]
    public int UserId { get; set; }


    [MaxLength(2000)]
    public string? Content { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreatedAt { get; set; }

    [MaxLength(50)]
    public string? Visibility { get; set; } = "Public"; // Public / Friends / Private

    public string? MediaUrl { get; set; } // Ảnh/Video (nếu có)

    public int? OriginalPostId { get; set; }

    [ForeignKey("OriginalPostId")]
    public virtual Post? OriginalPost { get; set; }

    [NotMapped] // Không ánh xạ vào database
    public IFormFile? ImageFile { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Posts")]
    public virtual User User { get; set; } = null!;

    [InverseProperty("Post")]
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    [InverseProperty("Post")]
    public virtual ICollection<Like> Likes { get; set; } = new List<Like>();
}
