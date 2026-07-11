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

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ZipCode { get; set; }
    public int? HomeCityId { get; set; }
    public string? HomeCityName { get; set; }
    public string? DominantHand { get; set; }
    public int? YearsPlaying { get; set; }
    public string? PreferredPlayStyle { get; set; }
    public string? AvatarId { get; set; }

    [NotMapped]
    public decimal CurrentDUPR => Math.Max(SinglesDUPR ?? 0m, DoublesDUPR ?? 0m);

    [NotMapped]
    public bool IsProfileComplete =>
        !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && !string.IsNullOrWhiteSpace(ZipCode)
        && SinglesDUPR.HasValue
        && DoublesDUPR.HasValue
        && TargetDUPR > 0m;

    public ICollection<UserDrillProgress> DrillProgresses { get; set; } = new List<UserDrillProgress>();
}
