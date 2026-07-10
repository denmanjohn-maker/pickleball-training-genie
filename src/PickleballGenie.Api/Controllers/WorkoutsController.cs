using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Api.Services;
using PickleballGenie.Data;
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

        var drills = await _context.Drills
            .Where(d => d.TargetDUPRLevel >= user.CurrentDUPR && d.TargetDUPRLevel <= user.TargetDUPR)
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
