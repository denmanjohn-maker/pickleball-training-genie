using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using Xunit;

namespace PickleballGenie.Tests;

public class MigrationSnapshotTests
{
    [Fact]
    public void ModelSnapshotMatchesTheCurrentModel()
    {
        // Migrations in this repo are sometimes written by hand (no `dotnet ef`
        // in every environment). EF Core throws at startup from Migrate() when
        // the model has drifted from the snapshot, which would take the API
        // down on deploy — so the check runs here first. Npgsql only needs a
        // connection string to build the model; nothing is opened.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var context = new AppDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "AppDbContext has model changes that no migration captures. Add a migration and update AppDbContextModelSnapshot.cs.");
    }
}
