using System;

namespace PickleballGenie.Models;

public enum DrillStatus
{
    InProgress,
    Mastered
}

public class UserDrillProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public Guid DrillId { get; set; }
    public Drill Drill { get; set; } = null!;

    public DrillStatus Status { get; set; } = DrillStatus.InProgress;
    public DateTime? CompletedAt { get; set; }
}
