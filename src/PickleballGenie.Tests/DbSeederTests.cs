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
        var refreshed = Assert.Single(drills, d => d.Id == Guid.Parse("a1000000-0000-0000-0000-000000000001"));
        // …it was refreshed with the current curated text and tags…
        var curated = CuratedDrills.GetAll().Single(d => d.Id == refreshed.Id);
        Assert.Equal(curated.Description, refreshed.Description);
        Assert.Equal(curated.MinPlayers, refreshed.MinPlayers);
        // …and the missing curated content (including the beginner tier) arrived.
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.0m);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 2.5m);
    }

    [Fact]
    public async Task SeedDrillsAsync_UpdatesExistingCuratedRowsInPlace_PreservingVideoUrlAndCreatedAt()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        var curated = CuratedDrills.GetAll().First(d => d.MinPlayers == 1 && d.Equipment != null);
        var createdAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.Drills.Add(new Drill
        {
            Id = curated.Id,
            Title = "Stale title",
            Description = "Stale description with no template.",
            Category = "General",
            TargetDUPRLevel = 5.0m,
            EstimatedDurationMinutes = 1,
            SourceUrl = "https://stale.example",
            MinPlayers = 2,
            Equipment = null,
            VideoUrl = "https://video.example/clip",
            CreatedAt = createdAt
        });
        await context.SaveChangesAsync();

        var (inserted, updated) = await DbSeeder.SeedDrillsAsync(context);

        var row = await context.Drills.SingleAsync(d => d.Id == curated.Id);
        Assert.Equal(curated.Title, row.Title);
        Assert.Equal(curated.Description, row.Description);
        Assert.Equal(curated.Category, row.Category);
        Assert.Equal(curated.TargetDUPRLevel, row.TargetDUPRLevel);
        Assert.Equal(curated.EstimatedDurationMinutes, row.EstimatedDurationMinutes);
        Assert.Equal(curated.SourceUrl, row.SourceUrl);
        Assert.Equal(curated.MinPlayers, row.MinPlayers);
        Assert.Equal(curated.Equipment, row.Equipment);
        // Fields the catalog doesn't own survive the refresh.
        Assert.Equal("https://video.example/clip", row.VideoUrl);
        Assert.Equal(createdAt, row.CreatedAt);

        Assert.Equal(1, updated);
        Assert.Equal(CuratedDrills.GetAll().Count - 1, inserted);
        Assert.Equal(CuratedDrills.GetAll().Count, await context.Drills.CountAsync());
    }

    [Fact]
    public async Task SeedDrillsAsync_DoesNotDuplicateWhenCuratedTitleCollidesWithScrapedTitle()
    {
        var options = GetInMemoryOptions();
        using var context = new AppDbContext(options);

        // The scraper may have inserted a page whose title happens to equal a
        // curated drill's. Upserting by Id must leave it alone and still add
        // the curated row — no PK clash, no title-based skip.
        var curated = CuratedDrills.GetAll()[0];
        var scraped = new Drill { Title = curated.Title, Category = "General", TargetDUPRLevel = 3.0m };
        context.Drills.Add(scraped);
        await context.SaveChangesAsync();

        await DbSeeder.SeedDrillsAsync(context);

        var sameTitle = await context.Drills.Where(d => d.Title == curated.Title).ToListAsync();
        Assert.Equal(2, sameTitle.Count);
        Assert.Contains(sameTitle, d => d.Id == scraped.Id);
        Assert.Contains(sameTitle, d => d.Id == curated.Id);
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
