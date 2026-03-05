using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocialNetwork.Models;

[Table("Comment")]
public partial class Comment
{
    [Key]
    public int CommentId { get; set; }

    public int PostId { get; set; }

    public int UserId { get; set; }

    public string? Content { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreatedAt { get; set; }

    public int? ParentCommentId { get; set; }

    [ForeignKey("PostId")]
    [InverseProperty("Comments")]
    public virtual Post Post { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("Comments")]
    public virtual User User { get; set; } = null!;

    [ForeignKey("ParentCommentId")]
    [InverseProperty("Replies")]
    public virtual Comment? ParentComment { get; set; }

    [InverseProperty("ParentComment")]
    public virtual ICollection<Comment> Replies { get; set; } = new List<Comment>();
}
