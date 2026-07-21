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

        var durationMinutes = request.DurationMinutes
            ?? user.PreferredSessionDurationMinutes
            ?? 30;

        durationMinutes = Math.Clamp(durationMinutes, 5, 180);

        var masteredDrillIds = _context.UserDrillProgresses
            .Where(p => p.UserId == userId && p.Status == DrillStatus.Mastered)
            .Select(p => p.DrillId);

        var drills = await _context.Drills
            .Where(d => d.TargetDUPRLevel >= user.CurrentDUPR && d.TargetDUPRLevel <= user.TargetDUPR)
            .Where(d => !masteredDrillIds.Contains(d.Id))
            .OrderBy(d => d.TargetDUPRLevel)
            .ThenBy(d => d.Category)
            .Take(20)
            .ToListAsync();

        if (!drills.Any())
            return BadRequest("No drills found for your DUPR range. Please run the scraper to populate the drill database.");

        try
        {
            var workoutPlan = await _llmService.GeneratePlanAsync(user, drills, durationMinutes);
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

        var session = new WorkoutSession
        {
            UserId = userId,
            CompletedAt = DateTime.UtcNow,
            DurationMinutes = request.DurationMinutes,
            Warmup = request.Warmup,
            Cooldown = request.Cooldown,
            Drills = request.Drills.Select((d, index) => new WorkoutSessionDrill
            {
                SortOrder = index,
                Title = d.Title,
                Category = d.Category,
                DurationMinutes = d.DurationMinutes,
                CoachingNotes = d.CoachingNotes,
                IsCompleted = d.IsCompleted
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

    private static WorkoutSessionResponse ToSessionResponse(WorkoutSession session) => new()
    {
        Id = session.Id.ToString(),
        CompletedAt = session.CompletedAt,
        DurationMinutes = session.DurationMinutes,
        Warmup = session.Warmup,
        Cooldown = session.Cooldown,
        Drills = session.Drills
            .OrderBy(d => d.SortOrder)
            .Select(d => new WorkoutSessionDrillResponse
            {
                Title = d.Title,
                Category = d.Category,
                DurationMinutes = d.DurationMinutes,
                CoachingNotes = d.CoachingNotes,
                IsCompleted = d.IsCompleted
            }).ToList()
    };
}

public class CompleteWorkoutSessionRequest
{
    public int DurationMinutes { get; set; }
    public string? Warmup { get; set; }
    public string? Cooldown { get; set; }
    public List<CompleteWorkoutSessionDrill> Drills { get; set; } = new();
}

public class CompleteWorkoutSessionDrill
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? CoachingNotes { get; set; }
    public bool IsCompleted { get; set; }
}

public class WorkoutSessionResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? Warmup { get; set; }
    public string? Cooldown { get; set; }
    public List<WorkoutSessionDrillResponse> Drills { get; set; } = new();
}

public class WorkoutSessionDrillResponse
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? CoachingNotes { get; set; }
    public bool IsCompleted { get; set; }
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
