using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Api.Services;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace PickleballGenie.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkoutsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWorkoutLlmService _llmService;

    public WorkoutsController(AppDbContext context, IWorkoutLlmService llmService)
    {
        _context = context;
        _llmService = llmService;
    }

    /// <summary>
    /// Generates a personalized drilling workout using an LLM.
    /// The workout is tailored to the authenticated user's current DUPR level, target DUPR level,
    /// and the requested session duration.
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateWorkout([FromBody] GenerateWorkoutRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound("User not found.");

        if (user.TargetDUPR < user.CurrentDUPR)
            return BadRequest("TargetDUPR must be greater than or equal to CurrentDUPR.");

        if (!PlayersAvailable.IsValid(request.PlayersAvailable))
            return BadRequest(PlayersAvailable.ValidationMessage);

        var durationMinutes = request.DurationMinutes
            ?? user.PreferredSessionDurationMinutes
            ?? 30;

        durationMinutes = Math.Clamp(durationMinutes, 5, 180);

        var masteredDrillIds = _context.UserDrillProgresses
            .Where(p => p.UserId == userId && p.Status == DrillStatus.Mastered)
            .Select(p => p.DrillId);

        var inRange = _context.Drills
            .Where(d => d.TargetDUPRLevel >= user.CurrentDUPR && d.TargetDUPRLevel <= user.TargetDUPR)
            .Where(d => !masteredDrillIds.Contains(d.Id));

        var playersAvailable = request.PlayersAvailable;
        var eligibleDrills = await inRange
            .Where(d => playersAvailable == null || d.MinPlayers <= playersAvailable)
            .ToListAsync();

        if (!eligibleDrills.Any())
        {
            // Tell the player what to change: the players filter is theirs to
            // flip, an empty DUPR range is an operator problem.
            if (playersAvailable != null && await inRange.AnyAsync())
                return BadRequest(playersAvailable == PlayersAvailable.Solo
                    ? "No solo drills found for your DUPR range yet. Try 'With a partner' or widen your target DUPR."
                    : "No drills found for your DUPR range and training group. Try a different 'Who's training?' choice or widen your target DUPR.");

            return BadRequest("No drills found for your DUPR range. Please run the scraper to populate the drill database.");
        }

        // Close the feedback loop: average the player's recent per-category
        // self-ratings so the LLM leans into skills they reported struggling
        // with. Sessions without ratings contribute nothing.
        var recentRatings = await _context.WorkoutSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CompletedAt)
            .Take(10)
            .SelectMany(s => s.Drills)
            .Where(d => d.SelfRating != null)
            .GroupBy(d => d.Category)
            .Select(g => new { Category = g.Key, Average = g.Average(d => (double)d.SelfRating!) })
            .ToDictionaryAsync(x => x.Category, x => x.Average);

        var drills = WorkoutDrillSelector.Select(
            eligibleDrills, recentRatings, WorkoutDrillSelector.DefaultMaxDrills, Random.Shared);

        try
        {
            var workoutPlan = await _llmService.GeneratePlanAsync(user, drills, durationMinutes, recentRatings, playersAvailable);
            EnrichPlan(workoutPlan, drills);
            return Ok(workoutPlan);
        }
        catch (WorkoutConfigurationException ex)
        {
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "Failed to reach the AI service.", details = ex.Message });
        }
        catch (WorkoutGenerationException ex)
        {
            return StatusCode(502, new { error = "AI service returned an error.", details = ex.Message });
        }
        catch (WorkoutDeserializationException ex)
        {
            return StatusCode(502, new { error = "AI returned a response that could not be parsed.", details = ex.Message, rawResponse = ex.RawResponse });
        }
    }

    /// <summary>
    /// Records a completed workout session (a snapshot of the generated plan plus
    /// which drills the user actually finished).
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> CompleteSession([FromBody] CompleteWorkoutSessionRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        if (request.Drills == null || request.Drills.Count == 0)
            return BadRequest(new { Message = "A workout session must contain at least one drill." });

        if (request.Drills.Any(d => d.SelfRating is < 1 or > 5))
            return BadRequest(new { Message = "SelfRating must be between 1 and 5." });

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > 2000 })
            return BadRequest(new { Message = "Notes must be 2000 characters or fewer." });

        var session = new WorkoutSession
        {
            UserId = userId,
            CompletedAt = DateTime.UtcNow,
            DurationMinutes = request.DurationMinutes,
            Warmup = request.Warmup,
            Cooldown = request.Cooldown,
            Notes = notes,
            Drills = request.Drills.Select((d, index) => new WorkoutSessionDrill
            {
                SortOrder = index,
                Title = d.Title,
                Category = d.Category,
                DurationMinutes = d.DurationMinutes,
                CoachingNotes = d.CoachingNotes,
                IsCompleted = d.IsCompleted,
                SelfRating = d.SelfRating
            }).ToList()
        };

        _context.WorkoutSessions.Add(session);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSessions), null, ToSessionResponse(session));
    }

    /// <summary>
    /// Returns the authenticated user's workout session history, newest first.
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.WorkoutSessions
            .Where(s => s.UserId == userId);

        var totalCount = await query.CountAsync();

        var sessions = await query
            .Include(s => s.Drills)
            .OrderByDescending(s => s.CompletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new WorkoutSessionListResponse
        {
            Items = sessions.Select(ToSessionResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Attaches the catalog drill behind each plan item so clients can show the
    /// full instructions, the solo/partner tag and equipment without a second
    /// request. The LLM is asked to echo each drill's list number; when it
    /// doesn't, fall back to an exact (case-insensitive) title match. Items
    /// that match nothing — the model invented or renamed a drill — keep nulls.
    /// </summary>
    internal static void EnrichPlan(WorkoutPlanResponse plan, IReadOnlyList<Drill> prompted)
    {
        foreach (var item in plan.Drills)
        {
            Drill? match = null;
            if (item.DrillNumber is int n && n >= 1 && n <= prompted.Count)
                match = prompted[n - 1];

            match ??= prompted.FirstOrDefault(d =>
                string.Equals(d.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match == null)
                continue;

            item.DrillId = match.Id.ToString();
            item.Title = match.Title;
            item.Category = match.Category;
            item.Description = match.Description;
            item.MinPlayers = match.MinPlayers;
            item.Equipment = match.Equipment;
        }
    }

    private static WorkoutSessionResponse ToSessionResponse(WorkoutSession session) => new()
    {
        Id = session.Id.ToString(),
        CompletedAt = session.CompletedAt,
        DurationMinutes = session.DurationMinutes,
        Warmup = session.Warmup,
        Cooldown = session.Cooldown,
        Notes = session.Notes,
        Drills = session.Drills
            .OrderBy(d => d.SortOrder)
            .Select(d => new WorkoutSessionDrillResponse
            {
                Title = d.Title,
                Category = d.Category,
                DurationMinutes = d.DurationMinutes,
                CoachingNotes = d.CoachingNotes,
                IsCompleted = d.IsCompleted,
                SelfRating = d.SelfRating
            }).ToList()
    };
}

public class CompleteWorkoutSessionRequest
{
    public int DurationMinutes { get; set; }
    public string? Warmup { get; set; }
    public string? Cooldown { get; set; }
    public string? Notes { get; set; }
    public List<CompleteWorkoutSessionDrill> Drills { get; set; } = new();
}

public class CompleteWorkoutSessionDrill
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? CoachingNotes { get; set; }
    public bool IsCompleted { get; set; }
    public int? SelfRating { get; set; }
}

public class WorkoutSessionResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? Warmup { get; set; }
    public string? Cooldown { get; set; }
    public string? Notes { get; set; }
    public List<WorkoutSessionDrillResponse> Drills { get; set; } = new();
}

public class WorkoutSessionDrillResponse
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? CoachingNotes { get; set; }
    public bool IsCompleted { get; set; }
    public int? SelfRating { get; set; }
}

public class WorkoutSessionListResponse
{
    public List<WorkoutSessionResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class GenerateWorkoutRequest
{
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Who the player has to train with: 1 = just me, 2 = one partner,
    /// 4 = a group of 3-4. Null leaves the catalog unfiltered.
    /// See <see cref="PlayersAvailable"/>.
    /// </summary>
    public int? PlayersAvailable { get; set; }
}

public class WorkoutDrillItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("coachingNotes")]
    public string CoachingNotes { get; set; } = string.Empty;

    /// <summary>1-based position in the drill list the LLM was shown; used to map the item back to its catalog drill.</summary>
    [JsonPropertyName("drillNumber")]
    public int? DrillNumber { get; set; }

    // Populated server-side from the matched catalog drill (null when the LLM
    // returned a title that matches nothing it was given).
    [JsonPropertyName("drillId")]
    public string? DrillId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("minPlayers")]
    public int? MinPlayers { get; set; }

    [JsonPropertyName("equipment")]
    public string? Equipment { get; set; }
}

public class WorkoutPlanResponse
{
    [JsonPropertyName("drills")]
    public List<WorkoutDrillItem> Drills { get; set; } = new();

    [JsonPropertyName("totalDuration")]
    public int TotalDuration { get; set; }

    [JsonPropertyName("warmup")]
    public string Warmup { get; set; } = string.Empty;

    [JsonPropertyName("cooldown")]
    public string Cooldown { get; set; } = string.Empty;

    [JsonPropertyName("coachingNotes")]
    public string CoachingNotes { get; set; } = string.Empty;
}
