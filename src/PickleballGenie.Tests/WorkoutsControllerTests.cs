using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PickleballGenie.Api.Controllers;
using PickleballGenie.Api.Services;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PickleballGenie.Tests;

public class WorkoutsControllerTests
{
    private DbContextOptions<AppDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static WorkoutsController BuildController(
        AppDbContext context,
        User user,
        HttpMessageHandler llmHandler,
        string? deepInfraApiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(deepInfraApiKey != null
                ? new Dictionary<string, string?> { ["DeepInfraApiKey"] = deepInfraApiKey }
                : new Dictionary<string, string?>())
            .Build();

        var httpClient = new HttpClient(llmHandler) { BaseAddress = new Uri("https://api.deepinfra.com/") };
        var llmService = new DeepInfraWorkoutLlmService(httpClient, config, NullLogger<DeepInfraWorkoutLlmService>.Instance);
        var controller = new WorkoutsController(context, llmService);

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };

        return controller;
    }

    private static WorkoutsController BuildController(
        AppDbContext context,
        User user,
        HttpResponseMessage llmResponse,
        string? deepInfraApiKey = "test-key")
        => BuildController(context, user, new StaticHttpMessageHandler(llmResponse), deepInfraApiKey);

    private static string ValidLlmResponse(string workoutJson) =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = workoutJson } } }
        });

    [Fact]
    public async Task GenerateWorkout_UsesRequestDuration_WhenProvided()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m, PreferredSessionDurationMinutes = 60 };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(async req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidLlmResponse("{\"drills\":[],\"totalDuration\":30,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var controller = BuildController(context, user, handler);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("30", captured); // prompt should reference the requested duration
    }

    [Fact]
    public async Task GenerateWorkout_FallsBackToPreferredDuration_WhenRequestOmitted()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m, PreferredSessionDurationMinutes = 45 };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(async req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidLlmResponse("{\"drills\":[],\"totalDuration\":45,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var controller = BuildController(context, user, handler);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = null });

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("45", captured);
    }

    [Fact]
    public async Task GenerateWorkout_ReturnsNoDrills_WhenDuprRangeEmpty()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        // Only a 4.0 drill — outside range
        context.Drills.Add(new Drill { Title = "Advanced", TargetDUPRLevel = 4.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var anyResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var controller = BuildController(context, user, anyResponse);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("DUPR", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task GenerateWorkout_Returns503_WhenApiKeyMissing()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var controller = BuildController(context, user, new HttpResponseMessage(), deepInfraApiKey: null);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task GenerateWorkout_Returns502_WhenLlmFails()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var errorResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid key\"}", Encoding.UTF8, "application/json")
        };
        var controller = BuildController(context, user, errorResponse);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, statusResult.StatusCode);
    }

    [Fact]
    public async Task GenerateWorkout_Returns400_WhenTargetDuprBelowCurrentDupr()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 4.0m, TargetDUPR = 3.0m };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var controller = BuildController(context, user, new HttpResponseMessage());

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GenerateWorkout_EmptyStringApiKeyInConfig_FallsBackToEnvVar()
    {
        // An empty string in config should be treated as unset, not as a valid key
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        // Config has a whitespace-only key — should NOT be used. No env var set either — should get 503.
        var controller = BuildController(context, user, new HttpResponseMessage(), deepInfraApiKey: "   ");

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task GenerateWorkout_DeserializesWorkoutPlan_WhenLlmWrapsResponseInMarkdownFences()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var workoutJson = "{\"drills\":[{\"title\":\"Dink\",\"category\":\"Dinking\",\"durationMinutes\":10,\"coachingNotes\":\"Focus on soft hands.\"}],\"totalDuration\":30,\"warmup\":\"Stretch\",\"cooldown\":\"Rest\",\"coachingNotes\":\"Great session.\"}";
        var fencedText = $"```json\n{workoutJson}\n```";
        var llmResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidLlmResponse(fencedText), Encoding.UTF8, "application/json")
        };
        var controller = BuildController(context, user, llmResponse);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var plan = Assert.IsType<WorkoutPlanResponse>(ok.Value);
        Assert.Single(plan.Drills);
        Assert.Equal("Dink", plan.Drills[0].Title);
    }

    [Fact]
    public async Task GenerateWorkout_ExcludesMasteredDrillsFromPrompt()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);

        var masteredDrill = new Drill { Title = "Mastered Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 };
        var unmasteredDrill = new Drill { Title = "Fresh Volley", TargetDUPRLevel = 3.5m, Category = "Volleys", EstimatedDurationMinutes = 10 };
        context.Drills.AddRange(masteredDrill, unmasteredDrill);
        await context.SaveChangesAsync();

        context.UserDrillProgresses.Add(new UserDrillProgress
        {
            UserId = userId,
            DrillId = masteredDrill.Id,
            Status = DrillStatus.Mastered,
            CompletedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(async req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidLlmResponse("{\"drills\":[],\"totalDuration\":30,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var controller = BuildController(context, user, handler);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        Assert.IsType<OkObjectResult>(result);
        Assert.DoesNotContain("Mastered Dink", captured);
        Assert.Contains("Fresh Volley", captured);
    }

    [Fact]
    public async Task CompleteSession_PersistsSelfRatingsAndNotes()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var controller = BuildController(context, user, new HttpResponseMessage(HttpStatusCode.OK));

        var request = new CompleteWorkoutSessionRequest
        {
            DurationMinutes = 30,
            Notes = "  Backhand dinks felt better today.  ",
            Drills = new List<CompleteWorkoutSessionDrill>
            {
                new() { Title = "Dink Ladder", Category = "Dinking", DurationMinutes = 10, IsCompleted = true, SelfRating = 2 },
                new() { Title = "Serve Targets", Category = "Serving", DurationMinutes = 10, IsCompleted = true, SelfRating = null }
            }
        };

        var result = await controller.CompleteSession(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<WorkoutSessionResponse>(created.Value);
        Assert.Equal("Backhand dinks felt better today.", response.Notes);
        Assert.Equal(2, response.Drills[0].SelfRating);
        Assert.Null(response.Drills[1].SelfRating);

        var stored = await context.WorkoutSessionDrills.FirstAsync(d => d.Title == "Dink Ladder");
        Assert.Equal(2, stored.SelfRating);
    }

    [Fact]
    public async Task CompleteSession_RejectsOutOfRangeSelfRating()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var controller = BuildController(context, user, new HttpResponseMessage(HttpStatusCode.OK));

        var request = new CompleteWorkoutSessionRequest
        {
            DurationMinutes = 30,
            Drills = new List<CompleteWorkoutSessionDrill>
            {
                new() { Title = "Dink Ladder", Category = "Dinking", DurationMinutes = 10, IsCompleted = true, SelfRating = 6 }
            }
        };

        var result = await controller.CompleteSession(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GenerateWorkout_IncludesRecentSelfRatingsInPrompt()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink Drill", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        context.WorkoutSessions.Add(new WorkoutSession
        {
            UserId = userId,
            CompletedAt = DateTime.UtcNow.AddDays(-1),
            DurationMinutes = 30,
            Drills = new List<WorkoutSessionDrill>
            {
                new() { SortOrder = 0, Title = "Old Dinks", Category = "Dinking", DurationMinutes = 10, IsCompleted = true, SelfRating = 1 }
            }
        });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(async req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidLlmResponse("{\"drills\":[],\"totalDuration\":30,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var controller = BuildController(context, user, handler);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("self-ratings", captured);
        Assert.Contains("Dinking: 1.0/5", captured);
    }

    [Fact]
    public async Task GenerateWorkout_Returns502_WhenAIResponseCannotBeParsed()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var llmResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidLlmResponse("Sorry, I cannot generate a workout right now."), Encoding.UTF8, "application/json")
        };
        var controller = BuildController(context, user, llmResponse);

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, statusResult.StatusCode);
    }
}

file class StaticHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    public StaticHttpMessageHandler(HttpResponseMessage response) => _response = response;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_response);
}

// Captures the request body and returns a dynamic response
file class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
    public CapturingHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _handler(request);
}
