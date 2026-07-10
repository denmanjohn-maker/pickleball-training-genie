using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace PickleballGenie.Models;

public class User : IdentityUser<Guid>
{
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? DuprAccountId { get; set; }
    public bool IsDuprLinked { get; set; }

    [NotMapped]
    public decimal CurrentDUPR => Math.Max(SinglesDUPR ?? 0m, DoublesDUPR ?? 0m);

    public ICollection<UserDrillProgress> DrillProgresses { get; set; } = new List<UserDrillProgress>();
}
