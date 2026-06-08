using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickleballGenie.Scraper;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Pickleball Drill Scraper...");

        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") 
            ?? "Host=localhost;Port=5432;Database=pickleball_genie;Username=postgres;Password=postgres";

        if (connectionString.StartsWith("postgres://"))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':');
            var password = userInfo.Length > 1 ? userInfo[1] : "";
            connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={password};SslMode=Require;TrustServerCertificate=True;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        using var dbContext = new AppDbContext(optionsBuilder.Options);

        Console.WriteLine("Ensuring database is migrated...");
        await dbContext.Database.MigrateAsync();

        Console.WriteLine("Scraping drills...");
        
        // This is a placeholder for actual scraping logic
        // E.g., loading a real webpage using HtmlWeb
        var newDrills = new[]
        {
            new Drill { Title = "Dink Consistency", Category = "Dinking", Description = "Cross-court dinking.", TargetDUPRLevel = 3.0m, SourceUrl = "https://example.com/drill1" },
            new Drill { Title = "Third Shot Drop", Category = "Drops", Description = "Drop from transition zone.", TargetDUPRLevel = 3.5m, SourceUrl = "https://example.com/drill2" },
            new Drill { Title = "Fast Hands Volley", Category = "Volleys", Description = "Kitchen line fire fight.", TargetDUPRLevel = 4.0m, SourceUrl = "https://example.com/drill3" }
        };

        foreach (var drill in newDrills)
        {
            if (!await dbContext.Drills.AnyAsync(d => d.Title == drill.Title))
            {
                dbContext.Drills.Add(drill);
                Console.WriteLine($"Added drill: {drill.Title}");
            }
        }

        await dbContext.SaveChangesAsync();
        Console.WriteLine("Scraping complete!");
    }
}
