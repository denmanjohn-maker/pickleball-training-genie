using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PickleballGenie.Scraper;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Pickleball Drill Scraper...");

        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=pickleball_genie;Username=postgres;Password=postgres";

        if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
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

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (compatible; PickleballGenieBot/1.0; +https://github.com/denmanjohn-maker/pickleball-training-genie)");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var allDrills = new List<Drill>();

        var sites = new[]
        {
            ("https://www.pickleballkitchen.com/drills/", "Pickleball Kitchen"),
            ("https://www.pickleballmax.com/category/drills/", "Pickleball Max"),
            ("https://3rdshotdrop.com/category/drills/", "3rd Shot Drop"),
        };

        foreach (var (url, siteName) in sites)
        {
            Console.WriteLine($"Scraping {siteName}...");
            try
            {
                var drills = await ScrapeSiteAsync(httpClient, url, siteName);
                Console.WriteLine($"  Found {drills.Count} drills from {siteName}");
                allDrills.AddRange(drills);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: failed to scrape {siteName}: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Fallback: always include a curated set of known drills so the DB is never empty
        allDrills.AddRange(GetCuratedDrills());

        Console.WriteLine($"Total drills collected: {allDrills.Count}. Upserting to database...");

        int added = 0;
        foreach (var drill in allDrills)
        {
            if (!await dbContext.Drills.AnyAsync(d => d.Title == drill.Title))
            {
                dbContext.Drills.Add(drill);
                Console.WriteLine($"  Added: [{drill.Category}] {drill.Title} (DUPR {drill.TargetDUPRLevel}, ~{drill.EstimatedDurationMinutes}min)");
                added++;
            }
        }

        await dbContext.SaveChangesAsync();
        Console.WriteLine($"Scraping complete! Added {added} new drills.");
    }

    static async Task<List<Drill>> ScrapeSiteAsync(HttpClient httpClient, string url, string siteName)
    {
        var drills = new List<Drill>();
        var web = new HtmlWeb();
        var doc = await web.LoadFromWebAsync(url);

        // Try multiple common blog/CMS patterns for article listings
        var articleNodes = doc.DocumentNode.SelectNodes(
            "//article | //div[contains(@class,'post')] | //div[contains(@class,'entry')]");

        if (articleNodes == null || !articleNodes.Any())
        {
            // Fallback: grab any heading links that look like drill titles
            var linkNodes = doc.DocumentNode.SelectNodes("//h2/a | //h3/a | //h4/a");
            if (linkNodes == null) return drills;

            foreach (var link in linkNodes.Take(30))
            {
                var title = HtmlEntity.DeEntitize(link.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length < 5) continue;

                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) href = url;

                var drill = BuildDrill(title, "", siteName, href);
                drills.Add(drill);
            }

            return drills;
        }

        foreach (var article in articleNodes.Take(30))
        {
            var titleNode = article.SelectSingleNode(".//h2/a | .//h3/a | .//h1/a | .//h2 | .//h3");
            if (titleNode == null) continue;

            var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(title) || title.Length < 5) continue;

            var descNode = article.SelectSingleNode(".//p | .//div[contains(@class,'excerpt')] | .//div[contains(@class,'summary')]");
            var description = descNode != null
                ? HtmlEntity.DeEntitize(descNode.InnerText).Trim()
                : "";

            var linkNode = titleNode.Name == "a" ? titleNode : article.SelectSingleNode(".//a[@href]");
            var sourceUrl = linkNode?.GetAttributeValue("href", url) ?? url;
            if (!sourceUrl.StartsWith("http")) sourceUrl = url;

            var drill = BuildDrill(title, description, siteName, sourceUrl);
            drills.Add(drill);
        }

        return drills;
    }

    static Drill BuildDrill(string title, string description, string siteName, string sourceUrl)
    {
        return new Drill
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(description)
                ? $"Pickleball drill: {title}. Source: {siteName}."
                : description,
            Category = MapCategory(title, description),
            TargetDUPRLevel = MapToDUPR(title, description),
            EstimatedDurationMinutes = EstimateDuration(title, description),
            SourceUrl = sourceUrl,
        };
    }

    static decimal MapToDUPR(string title, string description)
    {
        var s = (title + " " + description).ToLower();
        if (s.Contains("pro ") || s.Contains("professional") || s.Contains("tournament") || s.Contains("5.0"))
            return 5.0m;
        if (s.Contains("advanced") || s.Contains("competitive") || s.Contains("4.0") || s.Contains("high-level"))
            return 4.0m;
        if (s.Contains("intermediate") || s.Contains("3.5") || s.Contains("transition") || s.Contains("improving"))
            return 3.5m;
        return 3.0m;
    }

    static int EstimateDuration(string title, string description)
    {
        var s = (title + " " + description).ToLower();
        if (s.Contains("quick") || s.Contains("warm") || s.Contains("cool") || s.Contains("short"))
            return 5;
        if (s.Contains("extended") || s.Contains("multi") || s.Contains("sequence") || s.Contains("series"))
            return 15;
        if (s.Contains("game") || s.Contains("scenario") || s.Contains("match") || s.Contains("simulation"))
            return 20;
        return 10;
    }

    static string MapCategory(string title, string description)
    {
        var s = (title + " " + description).ToLower();
        if (s.Contains("dink")) return "Dinking";
        if (s.Contains("third shot") || s.Contains("3rd shot") || (s.Contains("drop") && !s.Contains("drop shot"))) return "Drops";
        if (s.Contains("drop shot")) return "Drops";
        if (s.Contains("volley")) return "Volleys";
        if (s.Contains("serve") || s.Contains("serving")) return "Serving";
        if (s.Contains("return")) return "Returns";
        if (s.Contains("lob")) return "Lobs";
        if (s.Contains("reset")) return "Resets";
        if (s.Contains("speed up") || s.Contains("attack") || s.Contains("punch")) return "Attacking";
        if (s.Contains("footwork") || s.Contains("movement")) return "Movement";
        return "General";
    }

    // Curated drills ensure the database always has quality content even if scraping fails.
    // These represent the full DUPR range and all major shot categories.
    static List<Drill> GetCuratedDrills() => new()
    {
        // 3.0 — Beginner
        new Drill { Title = "Cross-Court Dink Rally", Category = "Dinking", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Stand at the kitchen line and sustain a cross-court dinking rally with a partner. Focus on soft hands, a continental grip, and keeping the ball below net height. Rally for 50 consecutive dinks before moving to backhand side.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Title = "Serve and Stay In", Category = "Serving", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Practice a consistent, deep serve targeting the back third of the service box. Partner returns; server focuses on a controlled third shot. Repeat 20 times each side. Consistency over power.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Title = "Kitchen Line Volley Exchange", Category = "Volleys", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Both players at the kitchen line exchanging slow, controlled volleys. Goal is zero errors in 30 consecutive exchanges. Keeps paddle up, ready position after every shot.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Title = "Return of Serve Deep Target", Category = "Returns", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Server feeds consistent serves. Returner practices driving deep returns to the server's backhand corner. Goal: 8 of 10 returns land in the back third. Advance to kitchen after return.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },

        // 3.5 — Intermediate
        new Drill { Title = "Third Shot Drop from Baseline", Category = "Drops", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "From the baseline, hit a third shot drop into the non-volley zone. Partner catches or lets it bounce. Focus on a low, soft arc that lands in the kitchen. Complete 3 sets of 10 reps each side.",
            SourceUrl = "https://pickleballkitchen.com/third-shot-drop/" },
        new Drill { Title = "Transition Zone Drop and Advance", Category = "Drops", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 15,
            Description = "Start at mid-court (transition zone). Hit a drop shot into the kitchen then advance to the kitchen line. Partner resets the ball from the kitchen. Repeat, practicing the split-step timing as you advance.",
            SourceUrl = "https://pickleballkitchen.com/transition-zone/" },
        new Drill { Title = "Dink Patterns: Straight and Cross", Category = "Dinking", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "Partner drill: alternate between straight-ahead dinks and cross-court dinks in a structured pattern (2 straight, 2 cross). Helps develop shot placement awareness. Run for 5-minute sets.",
            SourceUrl = "https://pickleballkitchen.com/dinking-drills/" },
        new Drill { Title = "Backhand Reset Under Pressure", Category = "Resets", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "Partner speeds up balls at your backhand from the kitchen. Your job is to absorb the pace and drop the ball softly into the kitchen — a reset. Focus on giving with the paddle on contact. 3 sets of 15.",
            SourceUrl = "https://3rdshotdrop.com/backhand-reset/" },

        // 4.0 — Advanced
        new Drill { Title = "Speed-Up Attack and Counter", Category = "Attacking", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 10,
            Description = "Player A speeds up a ball at Player B's body. Player B counters back at Player A's feet. Rally continues until someone misses or resets. Develops quick reflexes and body-ball recognition. 5-minute continuous play.",
            SourceUrl = "https://pickleballmax.com/speed-up-drill/" },
        new Drill { Title = "Erne Setup and Execution", Category = "Volleys", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Practice the Erne: dink cross-court to pull opponent wide, then jump around the post or step through the kitchen corner to volley from outside the NVZ. Shadow practice 10 times then live reps with a feeding partner.",
            SourceUrl = "https://pickleballmax.com/erne-drill/" },
        new Drill { Title = "Advanced Third Shot Drop Sequences", Category = "Drops", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Baseline player hits third shot drops until one lands in the kitchen and is unattackable, then advances. Kitchen player applies pressure — speeds up anything above the net. Measures quality of drop under pressure. Competitive scoring.",
            SourceUrl = "https://pickleballkitchen.com/advanced-third-shot/" },
        new Drill { Title = "Competitive Dinking with Speed-Up Option", Category = "Dinking", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Cross-court dinking rally where either player can speed up at any time. The other player must counter or reset. Play to 11 points. Builds patience and the ability to recognize and create opportunities.",
            SourceUrl = "https://3rdshotdrop.com/competitive-dinking/" },

        // 5.0 — Professional
        new Drill { Title = "ATP and Roll Volley Combination", Category = "Attacking", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 15,
            Description = "Advanced tournament drill: partner feeds wide balls to practice the Around-The-Post (ATP) shot followed immediately by a roll volley to the open court. Execute 20 reps each side with correct footwork and follow-through.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Title = "Full-Court Scenario Game Simulation", Category = "General", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 20,
            Description = "Professional-level competitive rally simulation: points start with a serve and play out at full pace. Emphasis on pattern play — third shot, advance, reset if needed, find and attack the first pop-up. Analyze patterns after each point. Best of 21.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Title = "High-Level Firefight Training", Category = "Volleys", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 10,
            Description = "Both players at kitchen line engaging in rapid-fire speed-up exchanges. No resets allowed — must counter or win the point. Trains competitive fast-hands reflexes at professional pace. 10-point games with full tracking.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Title = "Serve plus Tournament Pattern Play", Category = "Serving", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 20,
            Description = "Professional serve targeting: practice power serves to T, body, and wide positions, each followed by a planned third shot pattern (drop vs. drive decision based on return depth). Full match-simulation pressure with scoring.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
    };
}
