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
    public int EstimatedDurationMinutes { get; set; } = 10;

    /// <summary>
    /// Fewest people needed to run the drill: 1 = can be done alone (wall,
    /// ball machine, self-feed, shadow work), 2 = needs one partner or feeder,
    /// 4 = needs a doubles group. Defaults to 2 so an untagged scraped drill is
    /// hidden from players training alone rather than wrongly offered to them.
    /// </summary>
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// Short comma-separated list of gear beyond paddle, ball and court, drawn
    /// from a small vocabulary: "Wall", "Ball machine", "Bucket of balls",
    /// "Cones or targets". Null means nothing extra is needed.
    /// </summary>
    public string? Equipment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserDrillProgress> UserProgresses { get; set; } = new List<UserDrillProgress>();
}
