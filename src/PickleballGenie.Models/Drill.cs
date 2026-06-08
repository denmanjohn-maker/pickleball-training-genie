using System;

namespace PickleballGenie.Models;

public class Drill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal TargetDUPRLevel { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? VideoUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserDrillProgress> UserProgresses { get; set; } = new List<UserDrillProgress>();
}
