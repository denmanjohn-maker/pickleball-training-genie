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
            SinglesDUPR = 3.0m,
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
        var result = await controller.GetRecommendations(null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var drills = Assert.IsAssignableFrom<IEnumerable<Drill>>(okResult.Value);
        
        Assert.Equal(2, drills.Count());
        Assert.Contains(drills, d => d.Title == "Drill 3.0");
        Assert.Contains(drills, d => d.Title == "Drill 3.5");
    }

    [Fact]
    public async Task GetRecommendations_ServesBeginnerTier_ForSub3Player()
    {
        // A 2.0 player targeting 2.5 gets the beginner-tier drills — the range
        // filter must work below DUPR 3.0.
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        var userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            Email = "newplayer@example.com",
            UserName = "newplayer@example.com",
            SinglesDUPR = 2.0m,
            TargetDUPR = 2.5m
        });

        context.Drills.AddRange(
            new Drill { Title = "Drill 2.0", TargetDUPRLevel = 2.0m },
            new Drill { Title = "Drill 2.5", TargetDUPRLevel = 2.5m },
            new Drill { Title = "Drill 3.0", TargetDUPRLevel = 3.0m }
        );
        await context.SaveChangesAsync();

        var controller = new DrillsController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                }, "mock"))
            }
        };

        // Act
        var result = await controller.GetRecommendations(null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var drills = Assert.IsAssignableFrom<IEnumerable<Drill>>(okResult.Value);

        Assert.Equal(2, drills.Count());
        Assert.Contains(drills, d => d.Title == "Drill 2.0");
        Assert.Contains(drills, d => d.Title == "Drill 2.5");
        Assert.DoesNotContain(drills, d => d.Title == "Drill 3.0");
    }

    [Fact]
    public async Task GetRecommendations_ExcludesMasteredDrills()
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
            SinglesDUPR = 3.0m,
            TargetDUPR = 3.5m
        };
        context.Users.Add(user);

        var masteredDrill = new Drill { Title = "Mastered Drill", TargetDUPRLevel = 3.0m };
        var unmasteredDrill = new Drill { Title = "Unmastered Drill", TargetDUPRLevel = 3.5m };
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
        var result = await controller.GetRecommendations(null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var drills = Assert.IsAssignableFrom<IEnumerable<Drill>>(okResult.Value);

        Assert.Single(drills);
        Assert.Contains(drills, d => d.Title == "Unmastered Drill");
        Assert.DoesNotContain(drills, d => d.Title == "Mastered Drill");
    }

    private static DrillsController ControllerFor(AppDbContext context, Guid userId)
    {
        var controller = new DrillsController(context);
        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = userClaims }
        };
        return controller;
    }

    private static void SeedMixedPlayers(AppDbContext context)
    {
        context.Drills.AddRange(
            new Drill { Title = "Solo Wall", TargetDUPRLevel = 3.0m, Category = "Dinking", MinPlayers = 1 },
            new Drill { Title = "Partner Feed", TargetDUPRLevel = 3.0m, Category = "Drops", MinPlayers = 2 },
            new Drill { Title = "Group Pattern", TargetDUPRLevel = 3.5m, Category = "Lobs", MinPlayers = 4 });
    }

    [Fact]
    public async Task GetDrills_FiltersByPlayersAvailable()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);
        SeedMixedPlayers(context);
        await context.SaveChangesAsync();
        var controller = new DrillsController(context);

        var solo = Assert.IsAssignableFrom<IEnumerable<Drill>>(
            Assert.IsType<OkObjectResult>(await controller.GetDrills(null, null, 1)).Value).ToList();
        var pair = Assert.IsAssignableFrom<IEnumerable<Drill>>(
            Assert.IsType<OkObjectResult>(await controller.GetDrills(null, null, 2)).Value).ToList();
        var all = Assert.IsAssignableFrom<IEnumerable<Drill>>(
            Assert.IsType<OkObjectResult>(await controller.GetDrills(null, null, null)).Value).ToList();

        Assert.Single(solo);
        Assert.Equal("Solo Wall", solo[0].Title);
        Assert.Equal(2, pair.Count);
        Assert.DoesNotContain(pair, d => d.Title == "Group Pattern");
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetDrills_Returns400_ForInvalidPlayersAvailable()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);
        var controller = new DrillsController(context);

        var result = await controller.GetDrills(null, null, 3);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetRecommendations_FiltersByPlayersAvailable()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);
        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, Email = "t@e.com", UserName = "t@e.com", SinglesDUPR = 3.0m, TargetDUPR = 3.5m });
        SeedMixedPlayers(context);
        await context.SaveChangesAsync();
        var controller = ControllerFor(context, userId);

        var solo = Assert.IsAssignableFrom<IEnumerable<Drill>>(
            Assert.IsType<OkObjectResult>(await controller.GetRecommendations(1)).Value).ToList();
        var group = Assert.IsAssignableFrom<IEnumerable<Drill>>(
            Assert.IsType<OkObjectResult>(await controller.GetRecommendations(4)).Value).ToList();

        Assert.Single(solo);
        Assert.Equal("Solo Wall", solo[0].Title);
        Assert.Equal(3, group.Count);

        Assert.IsType<BadRequestObjectResult>(await controller.GetRecommendations(3));
    }
}
