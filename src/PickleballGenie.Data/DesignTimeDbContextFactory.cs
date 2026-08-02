using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PickleballGenie.Data;

/// Lets `dotnet ef migrations add` build the context without booting the API
/// host or needing a live database — migration generation only reads the
/// model. The connection string is a placeholder and is never opened.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only;Username=design;Password=design")
            .Options;
        return new AppDbContext(options);
    }
}
