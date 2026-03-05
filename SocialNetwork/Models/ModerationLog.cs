using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocialNetwork.Models;

[Table("ModerationLog")]
public partial class ModerationLog
{
    [Key]
    public int LogId { get; set; }

    [StringLength(20)]
    public string TargetType { get; set; } = null!;

    public int TargetId { get; set; }

    public int UserId { get; set; }

    [StringLength(255)]
    public string? Reason { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreatedAt { get; set; }

    public bool? IsReviewed { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("ModerationLogs")]
    public virtual User User { get; set; } = null!;
}
