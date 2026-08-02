using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PickleballGenie.Api.Controllers;
using PickleballGenie.Models;

namespace PickleballGenie.Api.Services;

public interface IWorkoutLlmService
{
    Task<WorkoutPlanResponse> GeneratePlanAsync(User user, List<Drill> drills, int durationMinutes);
}

public class DeepInfraWorkoutLlmService : IWorkoutLlmService
{
    private const string DefaultModel = "deepseek-ai/DeepSeek-V3";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeepInfraWorkoutLlmService> _logger;

    public DeepInfraWorkoutLlmService(HttpClient httpClient, IConfiguration configuration, ILogger<DeepInfraWorkoutLlmService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<WorkoutPlanResponse> GeneratePlanAsync(User user, List<Drill> drills, int durationMinutes)
    {
        var configKey = _configuration["DeepInfraApiKey"];
        var apiKey = !string.IsNullOrWhiteSpace(configKey)
            ? configKey
            : Environment.GetEnvironmentVariable("DEEPINFRA_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new WorkoutConfigurationException("Workout generation is not configured. Please set DEEPINFRA_API_KEY.");

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
            model = _configuration["DeepInfra:Model"] ?? DefaultModel,
            max_tokens = 2000,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "You are an expert pickleball coach who creates structured, practical workout plans. Always respond with valid JSON matching the requested schema exactly — no markdown fences, no extra text." },
                new { role = "user", content = userPrompt }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/openai/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DeepInfra request failed: {StatusCode} - {Body}", response.StatusCode, responseJson);
            throw new WorkoutGenerationException(responseJson);
        }

        using var doc = JsonDocument.Parse(responseJson);
        var textContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
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
            if (workoutPlan == null)
                throw new WorkoutDeserializationException("AI returned a null response.", textContent);
            return workoutPlan;
        }
        catch (WorkoutDeserializationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WorkoutDeserializationException($"Failed to parse AI response: {ex.Message}", textContent);
        }
    }

    private static string DUPRLabel(decimal dupr) => dupr switch
    {
        <= 2.0m => "New to the game",
        <= 2.5m => "Advanced beginner",
        <= 3.0m => "Beginner",
        <= 3.5m => "Intermediate",
        <= 4.0m => "Advanced",
        _ => "Professional"
    };
}

public class WorkoutConfigurationException : Exception
{
    public WorkoutConfigurationException(string message) : base(message) { }
}

public class WorkoutGenerationException : Exception
{
    public WorkoutGenerationException(string message) : base(message) { }
}

public class WorkoutDeserializationException : Exception
{
    public string RawResponse { get; }
    public WorkoutDeserializationException(string message, string rawResponse) : base(message)
    {
        RawResponse = rawResponse;
    }
}
