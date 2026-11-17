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
    public void CatalogHasTwentyDrillsPerLevel()
    {
        var drills = CuratedDrills.GetAll();
        Assert.Equal(120, drills.Count);
        foreach (var level in ValidLevels)
            Assert.Equal(20, drills.Count(d => d.TargetDUPRLevel == level));
    }

    [Fact]
    public void IdsFollowThePinnedSequence()
    {
        // DbSeeder upserts by Id, so every curated drill must sit in the
        // a1000000-...-NNN sequence with no gaps: a gap means a drill was
        // deleted or renumbered, which would orphan rows in deployed databases.
        var drills = CuratedDrills.GetAll();
        var expected = Enumerable.Range(1, drills.Count)
            .Select(n => Guid.Parse($"a1000000-0000-0000-0000-{n:D12}"))
            .ToHashSet();
        Assert.Equal(expected, drills.Select(d => d.Id).ToHashSet());
    }

    [Fact]
    public void MinPlayersIsOneTwoOrFour()
    {
        var drills = CuratedDrills.GetAll();
        Assert.All(drills, d => Assert.Contains(d.MinPlayers, new[] { 1, 2, 4 }));
    }

    [Fact]
    public void EveryLevelHasAtLeastEightSoloDrills()
    {
        // The player who can't get a coach usually can't get a drilling partner
        // either, at every level — not just as a beginner.
        var drills = CuratedDrills.GetAll();
        foreach (var level in ValidLevels)
            Assert.True(
                drills.Count(d => d.TargetDUPRLevel == level && d.MinPlayers == 1) >= 8,
                $"Level {level} needs at least eight solo drills.");
    }

    [Fact]
    public void EveryLevelHasPartnerDrills_AndGroupDrillsExistAtAdvancedLevels()
    {
        var drills = CuratedDrills.GetAll();
        foreach (var level in ValidLevels)
            Assert.Contains(drills, d => d.TargetDUPRLevel == level && d.MinPlayers == 2);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 4.0m && d.MinPlayers == 4);
        Assert.Contains(drills, d => d.TargetDUPRLevel == 5.0m && d.MinPlayers == 4);
    }

    [Fact]
    public void DescriptionsFollowTheTemplate()
    {
        // Clients split the description on these labels to render sections,
        // and the LLM prompt relies on the same structure being present.
        var labels = new[] { "Setup:", "How to run it:", "Goal:", "Coaching cue:" };
        var drills = CuratedDrills.GetAll();
        Assert.All(drills, d =>
        {
            var last = -1;
            foreach (var label in labels)
            {
                var index = d.Description.IndexOf(label, StringComparison.Ordinal);
                Assert.True(index > last, $"\"{d.Title}\" is missing or misorders '{label}'.");
                last = index;
            }
            // A one-sentence summary precedes the sections.
            Assert.True(d.Description.IndexOf("Setup:", StringComparison.Ordinal) > 20,
                $"\"{d.Title}\" needs a summary sentence before 'Setup:'.");
        });
    }

    [Fact]
    public void SoloDrillsDoNotRequireAPartner()
    {
        // A solo drill's text may mention a partner only to say one isn't needed;
        // otherwise a player training alone gets instructions they can't follow.
        var solo = CuratedDrills.GetAll().Where(d => d.MinPlayers == 1);
        Assert.All(solo, d =>
        {
            var text = d.Description.ToLowerInvariant();
            foreach (var negated in new[] { "no partner", "without a partner", "partner optional", "partner-free", "if a partner" })
                text = text.Replace(negated, " ");
            Assert.DoesNotContain("partner", text);
            Assert.DoesNotContain("feeder", text);
        });
    }

    [Fact]
    public void EquipmentUsesTheSharedVocabulary()
    {
        // Clients render equipment as pills and the prompt lists it verbatim,
        // so spellings must stay consistent.
        var allowed = new[] { "Wall", "Ball machine", "Bucket of balls", "Cones or targets" };
        var drills = CuratedDrills.GetAll().Where(d => d.Equipment != null);
        Assert.All(drills, d =>
            Assert.All(d.Equipment!.Split(',', StringSplitOptions.TrimEntries),
                item => Assert.Contains(item, allowed)));
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

        // From 3.0 up, every category is available at every level.
        foreach (var level in ValidLevels.Where(l => l >= 3.0m))
            foreach (var category in ValidCategories)
                Assert.True(
                    drills.Any(d => d.Category == category && d.TargetDUPRLevel == level),
                    $"No {category} drill at DUPR {level}.");
    }
}
