using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Api.Controllers;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace PickleballGenie.Tests;

public class DrillsControllerTests
{
    private DbContextOptions<AppDbContext> GetInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task GetRecommendations_ReturnsDrillsWithinDuprRange()
    {
        // Arrange
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com",
            CurrentDUPR = 3.0m,
            TargetDUPR = 3.5m
        };
        context.Users.Add(user);

        context.Drills.AddRange(
            new Drill { Title = "Drill 2.5", TargetDUPRLevel = 2.5m },
            new Drill { Title = "Drill 3.0", TargetDUPRLevel = 3.0m },
            new Drill { Title = "Drill 3.5", TargetDUPRLevel = 3.5m },
            new Drill { Title = "Drill 4.0", TargetDUPRLevel = 4.0m }
        );
        await context.SaveChangesAsync();

        var controller = new DrillsController(context);
        
        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = userClaims }
        };

        // Act
        var result = await controller.GetRecommendations();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var drills = Assert.IsAssignableFrom<IEnumerable<Drill>>(okResult.Value);
        
        Assert.Equal(2, drills.Count());
        Assert.Contains(drills, d => d.Title == "Drill 3.0");
        Assert.Contains(drills, d => d.Title == "Drill 3.5");
    }
}
