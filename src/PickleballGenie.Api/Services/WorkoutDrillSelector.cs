using PickleballGenie.Models;

namespace PickleballGenie.Api.Services;

/// <summary>
/// Picks which eligible drills go into the LLM workout prompt. Selection is
/// category-stratified (round-robin across categories so every skill area is
/// represented), weakness-weighted (categories the player self-rated lowest
/// drain first when the cap binds), and shuffled within each category so the
/// whole catalog surfaces across repeated generations instead of the same
/// deterministic head.
/// </summary>
public static class WorkoutDrillSelector
{
    // Caps the prompt size: each drill line (title + full description) is
    // roughly 100–120 tokens, so 24 drills keeps the prompt near 3k tokens
    // while still giving the LLM two-plus drills per category to choose from.
    public const int DefaultMaxDrills = 24;

    private const double NeutralRating = 3.0;

    public static List<Drill> Select(
        IReadOnlyList<Drill> candidates,
        IReadOnlyDictionary<string, double>? categoryRatings,
        int maxDrills,
        Random rng)
    {
        if (candidates.Count <= maxDrills)
            return candidates.ToList();

        var groups = candidates
            .GroupBy(d => d.Category)
            .Select(g =>
            {
                var drills = g.ToList();
                Shuffle(drills, rng);
                var rating = categoryRatings != null && categoryRatings.TryGetValue(g.Key, out var r)
                    ? r
                    : NeutralRating;
                return (Category: g.Key, Rating: rating, Drills: drills);
            })
            .OrderBy(g => g.Rating)
            .ThenBy(g => g.Category, StringComparer.Ordinal)
            .ToList();

        var selected = new List<Drill>(maxDrills);
        for (var pass = 0; selected.Count < maxDrills; pass++)
        {
            var tookAny = false;
            foreach (var group in groups)
            {
                if (pass >= group.Drills.Count)
                    continue;
                selected.Add(group.Drills[pass]);
                tookAny = true;
                if (selected.Count == maxDrills)
                    break;
            }
            if (!tookAny)
                break;
        }

        return selected;
    }

    private static void Shuffle(List<Drill> drills, Random rng)
    {
        for (var i = drills.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (drills[i], drills[j]) = (drills[j], drills[i]);
        }
    }
}
