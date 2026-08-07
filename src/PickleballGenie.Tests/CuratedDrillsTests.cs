using PickleballGenie.Data;
using Xunit;

namespace PickleballGenie.Tests;

public class CuratedDrillsTests
{
    private static readonly string[] ValidCategories =
    {
        "Dinking", "Drops", "Volleys", "Serving", "Returns",
        "Lobs", "Resets", "Attacking", "Movement", "General"
    };

    private static readonly decimal[] ValidLevels = { 2.0m, 2.5m, 3.0m, 3.5m, 4.0m, 5.0m };

    [Fact]
    public void AllIdsAreUnique()
    {
        var drills = CuratedDrills.GetAll();
        Assert.Equal(drills.Count, drills.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public void AllTitlesAreUnique()
    {
        // The scraper dedupes against the database by Title, so a duplicate
        // title would silently drop a curated drill.
        var drills = CuratedDrills.GetAll();
        Assert.Equal(drills.Count, drills.Select(d => d.Title).Distinct().Count());
    }

    [Fact]
    public void AllCategoriesAreFromTheWhitelist()
    {
        // The self-rating feedback loop averages by exact Category string;
        // a novel spelling would fragment the adaptive-workout signal.
        var drills = CuratedDrills.GetAll();
        Assert.All(drills, d => Assert.Contains(d.Category, ValidCategories));
    }

    [Fact]
    public void AllLevelsAreValidDuprLevels()
    {
        var drills = CuratedDrills.GetAll();
        Assert.All(drills, d => Assert.Contains(d.TargetDUPRLevel, ValidLevels));
    }

    [Fact]
    public void AllDrillsHaveContent()
    {
        var drills = CuratedDrills.GetAll();
        Assert.All(drills, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Title));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.False(string.IsNullOrWhiteSpace(d.SourceUrl));
            Assert.True(d.EstimatedDurationMinutes > 0);
        });
    }

    [Fact]
    public void CatalogHasTwelveDrillsPerLevel()
    {
        var drills = CuratedDrills.GetAll();
        Assert.Equal(72, drills.Count);
        foreach (var level in ValidLevels)
            Assert.Equal(12, drills.Count(d => d.TargetDUPRLevel == level));
    }

    [Fact]
    public void BeginnerTierDrillsAreSoloFriendlyOrPartnerLabelled()
    {
        // Per CLAUDE.md, the 2.0/2.5 tier is deliberately solo-friendly.
        // Every drill there must say whether it can be done alone or needs a partner.
        var drills = CuratedDrills.GetAll().Where(d => d.TargetDUPRLevel <= 2.5m);
        Assert.All(drills, d =>
            Assert.True(
                d.Description.Contains("solo", StringComparison.OrdinalIgnoreCase)
                    || d.Description.Contains("partner", StringComparison.OrdinalIgnoreCase),
                $"\"{d.Title}\" must mention 'solo' or 'partner' in its description."));
    }

    [Fact]
    public void CoverageGapsAreClosed()
    {
        var drills = CuratedDrills.GetAll();

        // Every category exists somewhere in the catalog (Lobs was empty).
        foreach (var category in ValidCategories)
            Assert.Contains(drills, d => d.Category == category);

        // Skill-building categories exist below intermediate level.
        foreach (var category in new[] { "Drops", "Attacking", "Resets", "Movement" })
            Assert.Contains(drills, d => d.Category == category && d.TargetDUPRLevel < 3.5m);

        // Neglected categories exist at advanced levels.
        foreach (var category in new[] { "Returns", "Movement", "Resets", "Lobs" })
            Assert.Contains(drills, d => d.Category == category && d.TargetDUPRLevel >= 4.0m);
    }
}
