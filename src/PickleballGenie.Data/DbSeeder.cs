using Microsoft.EntityFrameworkCore;
using PickleballGenie.Models;

namespace PickleballGenie.Data;

public static class DbSeeder
{
    /// <summary>
    /// Upserts the curated drill catalog by pinned Id. Curated drills are the
    /// source of truth for their own rows: missing ones are inserted, and rows
    /// that already exist get their Title, Description, Category, level,
    /// duration, SourceUrl, MinPlayers and Equipment refreshed so rewritten
    /// instructions and new tags reach databases seeded before they existed.
    /// VideoUrl and CreatedAt on existing rows are left alone, and drills the
    /// scraper inserted under their own random Ids are never touched.
    /// Runs on every API and Scraper startup, so manual edits to a pinned-Id
    /// row are overwritten on the next deploy — edit CuratedDrills.cs instead.
    /// </summary>
    /// <returns>How many rows were inserted and how many existing rows were refreshed.</returns>
    public static async Task<(int Inserted, int Updated)> SeedDrillsAsync(AppDbContext dbContext)
    {
        var curated = CuratedDrills.GetAll();
        var curatedIds = curated.Select(d => d.Id).ToList();
        var existing = await dbContext.Drills
            .Where(d => curatedIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        var inserted = 0;
        var updated = 0;
        foreach (var drill in curated)
        {
            if (existing.TryGetValue(drill.Id, out var row))
            {
                if (CopyCuratedFields(drill, row))
                    updated++;
            }
            else
            {
                dbContext.Drills.Add(drill);
                inserted++;
            }
        }

        if (inserted > 0 || updated > 0)
            await dbContext.SaveChangesAsync();

        return (inserted, updated);
    }

    /// <summary>Copies the curated content onto an existing row; true when anything changed.</summary>
    private static bool CopyCuratedFields(Drill source, Drill target)
    {
        var changed = false;

        if (target.Title != source.Title) { target.Title = source.Title; changed = true; }
        if (target.Description != source.Description) { target.Description = source.Description; changed = true; }
        if (target.Category != source.Category) { target.Category = source.Category; changed = true; }
        if (target.TargetDUPRLevel != source.TargetDUPRLevel) { target.TargetDUPRLevel = source.TargetDUPRLevel; changed = true; }
        if (target.EstimatedDurationMinutes != source.EstimatedDurationMinutes) { target.EstimatedDurationMinutes = source.EstimatedDurationMinutes; changed = true; }
        if (target.SourceUrl != source.SourceUrl) { target.SourceUrl = source.SourceUrl; changed = true; }
        if (target.MinPlayers != source.MinPlayers) { target.MinPlayers = source.MinPlayers; changed = true; }
        if (target.Equipment != source.Equipment) { target.Equipment = source.Equipment; changed = true; }

        return changed;
    }
}
