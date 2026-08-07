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
    static async Task<int> Main(string[] args)
    {
        try
        {
            await Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FATAL: Unhandled exception — {ex.GetType().Name}: {ex.Message}");
            Log(ex.StackTrace ?? "(no stack trace)");
            return 1;
        }
    }

    static async Task Run()
    {
        Log("=== Pickleball Drill Scraper starting ===");

        var rawUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(rawUrl))
        {
            Log("WARNING: DATABASE_URL is not set — falling back to local connection string");
        }
        else
        {
            var masked = rawUrl.Length > 20 ? rawUrl[..20] + "***" : "***";
            Log($"DATABASE_URL detected: {masked}");
        }

        var connectionString = rawUrl
            ?? "Host=localhost;Port=5432;Database=pickleball_genie;Username=postgres;Password=postgres";

        if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
        {
            Log("Parsing Railway postgres:// connection string...");
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':');
            var password = userInfo.Length > 1 ? userInfo[1] : "";
            var sslMode = uri.Host.EndsWith(".railway.internal") ? "SslMode=Disable" : "SslMode=Require;TrustServerCertificate=True";
            connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={password};{sslMode};";
            Log($"Resolved host: {uri.Host}:{uri.Port}, database: {uri.LocalPath.TrimStart('/')}, ssl: {sslMode}");
        }

        Log("Building DbContext...");
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        using var dbContext = new AppDbContext(optionsBuilder.Options);

        Log("Connecting to database and applying pending migrations...");
        try
        {
            await dbContext.Database.MigrateAsync();
            Log("Migrations applied successfully.");
        }
        catch (Exception ex)
        {
            Log($"ERROR applying migrations: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        var existingCount = await dbContext.Drills.CountAsync();
        Log($"Database currently has {existingCount} drills.");

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
            Log($"--- Scraping {siteName} ({url}) ---");
            try
            {
                var drills = await ScrapeSiteAsync(httpClient, url, siteName);
                Log($"  Scraped {drills.Count} drills from {siteName}");
                allDrills.AddRange(drills);
            }
            catch (TaskCanceledException)
            {
                Log($"  TIMEOUT scraping {siteName} (>{httpClient.Timeout.TotalSeconds}s) — skipping");
            }
            catch (HttpRequestException ex)
            {
                Log($"  HTTP ERROR scraping {siteName}: {ex.StatusCode} — {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"  ERROR scraping {siteName}: {ex.GetType().Name}: {ex.Message}");
            }

            Log($"  Waiting 2s before next site...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Log($"--- Loading curated fallback drills ---");
        var curated = GetCuratedDrills();
        Log($"  {curated.Count} curated drills loaded");
        allDrills.AddRange(curated);

        Log($"Total drills collected (scraped + curated): {allDrills.Count}");

        Log("Loading existing drill titles from database...");
        var existingTitles = await dbContext.Drills
            .Select(d => d.Title)
            .ToHashSetAsync();
        Log($"  {existingTitles.Count} existing titles loaded");

        int added = 0;
        int skipped = 0;
        foreach (var drill in allDrills)
        {
            if (!existingTitles.Contains(drill.Title))
            {
                dbContext.Drills.Add(drill);
                existingTitles.Add(drill.Title);
                Log($"  [NEW] [{drill.Category}] {drill.Title} (DUPR {drill.TargetDUPRLevel}, ~{drill.EstimatedDurationMinutes}min)");
                added++;
            }
            else
            {
                skipped++;
            }
        }

        Log($"Saving {added} new drills to database ({skipped} skipped as duplicates)...");
        await dbContext.SaveChangesAsync();
        Log($"=== Scraper complete. Added: {added}, Skipped: {skipped}, Total in DB: {existingCount + added} ===");
    }

    static async Task<List<Drill>> ScrapeSiteAsync(HttpClient httpClient, string url, string siteName)
    {
        var drills = new List<Drill>();

        Log($"  Fetching HTML from {url}...");
        var html = await httpClient.GetStringAsync(url);
        Log($"  Received {html.Length:N0} bytes");

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var articleNodes = doc.DocumentNode.SelectNodes(
            "//article | //div[contains(@class,'post')] | //div[contains(@class,'entry')]");

        if (articleNodes == null || !articleNodes.Any())
        {
            Log($"  No article nodes found — trying heading link fallback");
            var linkNodes = doc.DocumentNode.SelectNodes("//h2/a | //h3/a | //h4/a");
            if (linkNodes == null)
            {
                Log($"  No heading links found either — 0 drills from {siteName}");
                return drills;
            }

            Log($"  Found {linkNodes.Count} heading links");
            foreach (var link in linkNodes.Take(30))
            {
                var title = HtmlEntity.DeEntitize(link.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length < 5) continue;

                var href = link.GetAttributeValue("href", "");
                var sourceUrl = string.IsNullOrEmpty(href) ? url : ResolveUrl(href, url);
                drills.Add(BuildDrill(title, "", siteName, sourceUrl));
            }

            return drills;
        }

        Log($"  Found {articleNodes.Count} article nodes");
        foreach (var article in articleNodes.Take(30))
        {
            var titleNode = article.SelectSingleNode(".//h2/a | .//h3/a | .//h1/a | .//h2 | .//h3");
            if (titleNode == null) continue;

            var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(title) || title.Length < 5) continue;

            var descNode = article.SelectSingleNode(".//p | .//div[contains(@class,'excerpt')] | .//div[contains(@class,'summary')]");
            var description = descNode != null ? HtmlEntity.DeEntitize(descNode.InnerText).Trim() : "";

            var linkNode = titleNode.Name == "a" ? titleNode : article.SelectSingleNode(".//a[@href]");
            var rawHref = linkNode?.GetAttributeValue("href", "") ?? "";
            var sourceUrl = string.IsNullOrEmpty(rawHref) ? url : ResolveUrl(rawHref, url);

            drills.Add(BuildDrill(title, description, siteName, sourceUrl));
        }

        return drills;
    }

    static void Log(string message) =>
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");

    static string ResolveUrl(string href, string baseUrl)
    {
        if (href.StartsWith("http://") || href.StartsWith("https://"))
            return href;
        return new Uri(new Uri(baseUrl), href).AbsoluteUri;
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

    // Curated drills live in PickleballGenie.Data.CuratedDrills, shared with
    // DbSeeder. The title-based dedupe below keeps re-runs idempotent.
    static List<Drill> GetCuratedDrills() => CuratedDrills.GetAll();
}
