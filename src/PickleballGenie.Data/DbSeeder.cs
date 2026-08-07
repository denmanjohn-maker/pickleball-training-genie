using Microsoft.EntityFrameworkCore;
using PickleballGenie.Models;

namespace PickleballGenie.Data;

public static class DbSeeder
{
    public static async Task SeedDrillsAsync(AppDbContext dbContext)
    {
        // Additive: insert any curated drills whose Ids aren't in the database
        // yet, so newly curated content (e.g. the 2.0/2.5 beginner tier)
        // reaches databases seeded before it existed. Existing rows — including
        // scraper-inserted drills with their own Ids — are never modified.
        var curated = CuratedDrills.GetAll();
        var curatedIds = curated.Select(d => d.Id).ToList();
        var existingIds = await dbContext.Drills
            .Where(d => curatedIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();

        var missing = curated.Where(d => !existingIds.Contains(d.Id)).ToList();
        if (missing.Count == 0)
            return;

        dbContext.Drills.AddRange(missing);
        await dbContext.SaveChangesAsync();
    }
}
