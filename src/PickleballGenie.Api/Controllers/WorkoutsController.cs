using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickleballGenie.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkoutsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public WorkoutsController(AppDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Generates a personalized drilling workout using Claude AI.
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

        var configKey = _configuration["AnthropicApiKey"];
        var apiKey = !string.IsNullOrWhiteSpace(configKey)
            ? configKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, "Workout generation is not configured. Please set ANTHROPIC_API_KEY.");

        try
        {
            var workoutPlan = await GenerateWithClaude(apiKey, user, drills, durationMinutes);
            return Ok(workoutPlan);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "Failed to reach the AI service.", details = ex.Message });
        }
        catch (WorkoutGenerationException ex)
        {
            return StatusCode(502, new { error = "AI service returned an error.", details = ex.Message });
        }
    }

    private async Task<object> GenerateWithClaude(string apiKey, User user, List<Drill> drills, int durationMinutes)
    {
        var drillList = string.Join("\n", drills.Select((d, i) =>
            $"{i + 1}. [{d.Category}] \"{d.Title}\" (~{d.EstimatedDurationMinutes} min, DUPR {d.TargetDUPRLevel}): {d.Description}"));

        var userPrompt = $$"""
            Create a {{durationMinutes}}-minute pickleball drilling workout for a player with:
            - Current DUPR: {{user.CurrentDUPR}} ({{DUPRLabel(user.CurrentDUPR)}})
            - Target DUPR: {{user.TargetDUPR}} ({{DUPRLabel(user.TargetDUPR)}})

            Available drills:
            {{drillList}}

            Select drills that fit within {{durationMinutes}} minutes total. Include warmup and cooldown time. Prioritize variety across categories. For each chosen drill, provide level-specific coaching notes relevant to a DUPR {{user.CurrentDUPR}} player working toward DUPR {{user.TargetDUPR}}.

            Respond with ONLY a valid JSON object matching this exact schema — no markdown, no explanation:
            {
              "drills": [
                {
                  "title": "string",
                  "category": "string",
                  "durationMinutes": 10,
                  "coachingNotes": "string"
                }
              ],
              "totalDuration": {{durationMinutes}},
              "warmup": "string",
              "cooldown": "string",
              "coachingNotes": "string"
            }
            """;

        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 2000,
            system = "You are an expert pickleball coach who creates structured, practical workout plans. Always respond with valid JSON matching the requested schema exactly — no markdown fences, no extra text.",
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var httpClient = _httpClientFactory.CreateClient("anthropic");
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        httpClient.DefaultRequestHeaders.Remove("x-api-key");
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

        var response = await httpClient.PostAsync("v1/messages", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new WorkoutGenerationException(responseJson);

        using var doc = JsonDocument.Parse(responseJson);
        var textContent = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        var strippedText = textContent.Trim();
        if (strippedText.StartsWith("```"))
        {
            var firstNewline = strippedText.IndexOf('\n');
            var lastFence = strippedText.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                strippedText = strippedText[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var workoutPlan = JsonSerializer.Deserialize<WorkoutPlanResponse>(strippedText, options);
            return (object?)workoutPlan ?? new { rawResponse = textContent };
        }
        catch
        {
            return new { rawResponse = textContent };
        }
    }

    private static string DUPRLabel(decimal dupr) => dupr switch
    {
        <= 3.0m => "Beginner",
        <= 3.5m => "Intermediate",
        <= 4.0m => "Advanced",
        _ => "Professional"
    };
}

public class WorkoutGenerationException : Exception
{
    public WorkoutGenerationException(string message) : base(message) { }
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
