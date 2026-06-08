using System;
using Microsoft.AspNetCore.Identity;

namespace PickleballGenie.Models;

public class User : IdentityUser<Guid>
{
    public decimal CurrentDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserDrillProgress> DrillProgresses { get; set; } = new List<UserDrillProgress>();
}
