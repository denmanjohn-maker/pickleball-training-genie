using System;

namespace PickleballGenie.Models;

public class WorkoutSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public int DurationMinutes { get; set; }
    public string? Warmup { get; set; }
    public string? Cooldown { get; set; }
    /// Free-form journal note the player attached when saving the session.
    public string? Notes { get; set; }

    public ICollection<WorkoutSessionDrill> Drills { get; set; } = new List<WorkoutSessionDrill>();
}

public class WorkoutSessionDrill
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkoutSessionId { get; set; }
    public WorkoutSession WorkoutSession { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? CoachingNotes { get; set; }
    public bool IsCompleted { get; set; }
    /// How the drill felt, 1 (struggled) to 5 (nailed it). Null when the
    /// player skipped rating — the feedback signal behind adaptive workouts.
    public int? SelfRating { get; set; }
}
