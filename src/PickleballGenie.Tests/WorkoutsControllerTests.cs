using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PickleballGenie.Api.Controllers;
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
        HttpResponseMessage anthropicResponse,
        string? anthropicApiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(anthropicApiKey != null
                ? new Dictionary<string, string?> { ["AnthropicApiKey"] = anthropicApiKey }
                : new Dictionary<string, string?>())
            .Build();

        var factory = new FakeHttpClientFactory(anthropicResponse);
        var controller = new WorkoutsController(context, factory, config);

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

    private static string ValidAnthropicResponse(string workoutJson) =>
        JsonSerializer.Serialize(new
        {
            content = new[] { new { text = workoutJson } }
        });

    [Fact]
    public async Task GenerateWorkout_UsesRequestDuration_WhenProvided()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m, PreferredSessionDurationMinutes = 60 };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidAnthropicResponse("{\"drills\":[],\"totalDuration\":30,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test" })
            .Build();

        var controller = new WorkoutsController(context, new FakeHttpClientFactory(handler), config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "mock"))
            }
        };

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
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m, PreferredSessionDurationMinutes = 45 };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var captured = "";
        var handler = new CapturingHttpMessageHandler(req =>
        {
            captured = req.Content != null ? await req.Content.ReadAsStringAsync() : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidAnthropicResponse("{\"drills\":[],\"totalDuration\":45,\"warmup\":\"\",\"cooldown\":\"\",\"coachingNotes\":\"\"}"), Encoding.UTF8, "application/json")
            };
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test" })
            .Build();

        var controller = new WorkoutsController(context, new FakeHttpClientFactory(handler), config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "mock"))
            }
        };

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
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m };
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
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build(); // no API key
        var controller = new WorkoutsController(context, new FakeHttpClientFactory(new HttpResponseMessage()), config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "mock"))
            }
        };

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task GenerateWorkout_Returns502_WhenAnthropicFails()
    {
        var options = InMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m };
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
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 4.0m, TargetDUPR = 3.0m };
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
        var user = new User { Id = userId, UserName = "u", Email = "u@t.com", CurrentDUPR = 3.0m, TargetDUPR = 3.5m };
        context.Users.Add(user);
        context.Drills.Add(new Drill { Title = "Dink", TargetDUPRLevel = 3.0m, Category = "Dinking", EstimatedDurationMinutes = 10 });
        await context.SaveChangesAsync();

        // Config has an empty string key — should NOT be used
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "   " })
            .Build();

        // No env var set either — should get 503
        var controller = new WorkoutsController(context, new FakeHttpClientFactory(new HttpResponseMessage()), config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "mock"))
            }
        };

        var result = await controller.GenerateWorkout(new GenerateWorkoutRequest { DurationMinutes = 30 });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }
}

// Fake IHttpClientFactory that returns an HttpClient backed by a fixed response
file class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpResponseMessage response)
        : this(new StaticHttpMessageHandler(response)) { }

    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        return client;
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
