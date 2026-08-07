using PickleballGenie.Api.Services;
using PickleballGenie.Models;
using Xunit;

namespace PickleballGenie.Tests;

public class WorkoutDrillSelectorTests
{
    private static List<Drill> MakeDrills(params (string Category, int Count)[] groups)
    {
        var drills = new List<Drill>();
        foreach (var (category, count) in groups)
            for (var i = 0; i < count; i++)
                drills.Add(new Drill { Title = $"{category} {i}", Category = category, TargetDUPRLevel = 3.0m });
        return drills;
    }

    [Fact]
    public void ReturnsAllCandidates_WhenUnderTheCap()
    {
        var drills = MakeDrills(("Dinking", 3), ("Serving", 2));

        var selected = WorkoutDrillSelector.Select(drills, null, 24, new Random(1));

        Assert.Equal(drills.Count, selected.Count);
        Assert.Equal(drills.Select(d => d.Id).OrderBy(id => id), selected.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public void NeverExceedsTheCap()
    {
        var drills = MakeDrills(("Dinking", 20), ("Serving", 20), ("Drops", 20));

        var selected = WorkoutDrillSelector.Select(drills, null, 24, new Random(1));

        Assert.Equal(24, selected.Count);
    }

    [Fact]
    public void EveryCategoryIsRepresented_WhenCapAllows()
    {
        var drills = MakeDrills(
            ("Dinking", 10), ("Serving", 10), ("Drops", 10), ("Volleys", 10),
            ("Returns", 10), ("Lobs", 10), ("Resets", 10), ("Attacking", 10),
            ("Movement", 10), ("General", 10));

        var selected = WorkoutDrillSelector.Select(drills, null, 24, new Random(7));

        Assert.Equal(10, selected.Select(d => d.Category).Distinct().Count());
    }

    [Fact]
    public void LowRatedCategoryDrainsFirst_WhenCapBinds()
    {
        // Two categories, three drills each, cap of 3: the round-robin visits
        // the weak category first each pass, so it contributes the extra drill.
        var drills = MakeDrills(("Dinking", 3), ("Serving", 3));
        var ratings = new Dictionary<string, double> { ["Serving"] = 1.5, ["Dinking"] = 4.5 };

        var selected = WorkoutDrillSelector.Select(drills, ratings, 3, new Random(1));

        Assert.Equal(3, selected.Count);
        Assert.Equal(2, selected.Count(d => d.Category == "Serving"));
        Assert.Equal(1, selected.Count(d => d.Category == "Dinking"));
    }

    [Fact]
    public void SameSeedProducesIdenticalSelection()
    {
        var drills = MakeDrills(("Dinking", 15), ("Serving", 15), ("Drops", 15));

        var first = WorkoutDrillSelector.Select(drills, null, 24, new Random(42));
        var second = WorkoutDrillSelector.Select(drills, null, 24, new Random(42));

        Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
    }

    [Fact]
    public void DifferentSeedsSurfaceDifferentDrills()
    {
        var drills = MakeDrills(("Dinking", 30), ("Serving", 30));

        var first = WorkoutDrillSelector.Select(drills, null, 10, new Random(1));
        var second = WorkoutDrillSelector.Select(drills, null, 10, new Random(2));

        Assert.NotEqual(first.Select(d => d.Id), second.Select(d => d.Id));
    }

    [Fact]
    public void StopsWhenAllCandidatesAreExhausted_BeforeReachingCap()
    {
        var drills = MakeDrills(("Dinking", 2), ("Serving", 1));

        var selected = WorkoutDrillSelector.Select(drills, null, 24, new Random(1));

        Assert.Equal(3, selected.Count);
    }
}
