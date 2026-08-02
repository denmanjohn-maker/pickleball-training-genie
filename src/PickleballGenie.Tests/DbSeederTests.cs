using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using PickleballGenie.Models;
using Xunit;

namespace PickleballGenie.Tests;

public class DbSeederTests
{
    private DbContextOptions<AppDbContext> GetInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task SeedDrillsAsync_SeedsAllCuratedDrills_IntoEmptyDatabase()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        await DbSeeder.SeedDrillsAsync(context);

        var drills = await context.Drills.ToListAsync();
        Assert.NotEmpty(drills);
        // The beginner tier must be present — it's the reason the seeder exists
        // for players below 3.0.
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.0m);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.5m);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 3.0m);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 5.0m);
    }

    [Fact]
    public async Task SeedDrillsAsync_AddsMissingCuratedDrills_ToPreviouslySeededDatabase()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        // Simulate a database seeded before the 2.0/2.5 tier existed: curated
        // 3.0+ drills present, nothing below 3.0.
        context.Drills.Add(new Drill
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000001"),
            Title = "Cross-Court Dink Rally",
            Category = "Dinking",
            TargetDUPRLevel = 3.0m
        });
        await context.SaveChangesAsync();

        await DbSeeder.SeedDrillsAsync(context);

        var drills = await context.Drills.ToListAsync();
        // The old row was not duplicated…
        Assert.Single(drills, d => d.Id == Guid.Parse("a1000000-0000-0000-0000-000000000001"));
        // …and the missing curated content (including the beginner tier) arrived.
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.0m);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.5m);
    }

    [Fact]
    public async Task SeedDrillsAsync_IsIdempotent()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        await DbSeeder.SeedDrillsAsync(context);
        var countAfterFirstRun = await context.Drills.CountAsync();

        await DbSeeder.SeedDrillsAsync(context);
        var countAfterSecondRun = await context.Drills.CountAsync();

        Assert.Equal(countAfterFirstRun, countAfterSecondRun);
    }

    [Fact]
    public async Task SeedDrillsAsync_LeavesScraperInsertedDrillsAlone()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        // A drill the scraper inserted with its own random Id.
        var scraped = new Drill
        {
            Title = "Scraped Special",
            Category = "General",
            TargetDUPRLevel = 3.5m
        };
        context.Drills.Add(scraped);
        await context.SaveChangesAsync();

        await DbSeeder.SeedDrillsAsync(context);

        var kept = await context.Drills.SingleOrDefaultAsync(d => d.Id == scraped.Id);
        Assert.NotNull(kept);
        Assert.Equal("Scraped Special", kept!.Title);
    }
}
