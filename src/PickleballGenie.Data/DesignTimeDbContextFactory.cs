using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PickleballGenie.Data;

/// Lets EF Core tooling (`dotnet ef migrations add`, `database update`, …)
/// build the context without booting the API host. Commands that only read
/// the model never open a connection; commands that touch a database (e.g.
/// `database update`) need a real connection string — provide one via the
/// EF_DESIGN_TIME_CONNECTION environment variable. The fallback below is a
/// non-functional placeholder, not a credential.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EF_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Database=design_time_only;Username=design_time_placeholder";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
