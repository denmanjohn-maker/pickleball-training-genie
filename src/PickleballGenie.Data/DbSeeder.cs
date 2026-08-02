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
        var curated = GetCuratedDrills();
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

    private static List<Drill> GetCuratedDrills() => new()
    {
        // 2.0 — New to the game. Deliberately solo-friendly: players without
        // access to coaching usually don't have a drilling partner either.
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000017"), Title = "Serve 20 In a Row", Category = "Serving", TargetDUPRLevel = 2.0m, EstimatedDurationMinutes = 10,
            Description = "Solo drill — all you need is a bucket of balls. From behind the baseline, serve into the correct service box 20 times in a row, using either a traditional underhand serve or the easier drop serve. Any depth counts; you're building a repeatable motion. Miss? Restart the count and track your best streak week to week.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000018"), Title = "Wall Dink Touch", Category = "Dinking", TargetDUPRLevel = 2.0m, EstimatedDurationMinutes = 10,
            Description = "Solo drill — any solid wall works. Mark a line at net height (34 inches). Stand about 7 feet back and softly dink above the line, letting the ball bounce between hits. Goal: 25 controlled dinks in a row. Soft grip, paddle out front, and bend your knees — not your wrist.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000019"), Title = "Drop, Bounce, Hit", Category = "General", TargetDUPRLevel = 2.0m, EstimatedDurationMinutes = 10,
            Description = "Solo drill for brand-new players. Drop the ball, let it bounce, and hit an easy forehand groundstroke across the court; collect and repeat, then switch to backhand. 20 each side. You're learning the paddle face, the contact point, and how the ball comes off the court — the foundation for everything else.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000020"), Title = "Let It Bounce: Return Habits", Category = "Returns", TargetDUPRLevel = 2.0m, EstimatedDurationMinutes = 10,
            Description = "With a partner feeding easy serves (or a ball rebounding off a wall), practice the two-bounce habit: let the serve bounce, return it, and call the bounce out loud. Ten returns from each side. This builds the rule into your reflexes before match play, where volleying the serve is the classic new-player fault.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },

        // 2.5 — Advanced beginner
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000021"), Title = "Deep Serve Targets", Category = "Serving", TargetDUPRLevel = 2.5m, EstimatedDurationMinutes = 10,
            Description = "Solo drill. Place a towel or cones across the back third of the service box. Serve 10 balls at each box, counting how many land deep past your marker. Goal: 7 of 10. A deep serve keeps the returner pinned back and buys you time — the first serve upgrade that wins points at this level.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000022"), Title = "Wall Volley Control", Category = "Volleys", TargetDUPRLevel = 2.5m, EstimatedDurationMinutes = 10,
            Description = "Solo drill. Stand 7–8 feet from a wall, paddle up in ready position, and volley continuously without letting the ball bounce. Start forehand-only, then alternate forehand and backhand. Goal: 25 in a row under control. Keep a short, compact punch — no backswing.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000023"), Title = "Baseline to Kitchen Footwork", Category = "Movement", TargetDUPRLevel = 2.5m, EstimatedDurationMinutes = 10,
            Description = "Solo drill. Start at the baseline, shadow a return, then advance toward the kitchen line in controlled steps with a split-step each time an imaginary opponent would strike the ball. Touch the line, backpedal to mid-court, split-step, and come forward again. 10 trips. Court movement is a skill — practice it like one.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000024"), Title = "Cooperative Dink Rally to 20", Category = "Dinking", TargetDUPRLevel = 2.5m, EstimatedDurationMinutes = 10,
            Description = "Partner drill. Both players at the kitchen line, dinking cross-court cooperatively — the goal is keeping the rally alive, not winning it. Count out loud to 20, then switch to the other diagonal. If a ball pops up high, catch it and restart. Height discipline now becomes attack prevention later.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },

        // 3.0 — Beginner
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000001"), Title = "Cross-Court Dink Rally", Category = "Dinking", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Stand at the kitchen line and sustain a cross-court dinking rally with a partner. Focus on soft hands, a continental grip, and keeping the ball below net height. Rally for 50 consecutive dinks before moving to backhand side.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000002"), Title = "Serve and Stay In", Category = "Serving", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Practice a consistent, deep serve targeting the back third of the service box. Partner returns; server focuses on a controlled third shot. Repeat 20 times each side. Consistency over power.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000003"), Title = "Kitchen Line Volley Exchange", Category = "Volleys", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Both players at the kitchen line exchanging slow, controlled volleys. Goal is zero errors in 30 consecutive exchanges. Keep paddle up, ready position after every shot.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000004"), Title = "Return of Serve Deep Target", Category = "Returns", TargetDUPRLevel = 3.0m, EstimatedDurationMinutes = 10,
            Description = "Server feeds consistent serves. Returner practices driving deep returns to the server's backhand corner. Goal: 8 of 10 returns land in the back third. Advance to kitchen after return.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/how-to-play/basics/" },

        // 3.5 — Intermediate
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000005"), Title = "Third Shot Drop from Baseline", Category = "Drops", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "From the baseline, hit a third shot drop into the non-volley zone. Partner catches or lets it bounce. Focus on a low, soft arc that lands in the kitchen. Complete 3 sets of 10 reps each side.",
            SourceUrl = "https://pickleballkitchen.com/third-shot-drop/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000006"), Title = "Transition Zone Drop and Advance", Category = "Drops", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 15,
            Description = "Start at mid-court (transition zone). Hit a drop shot into the kitchen then advance to the kitchen line. Partner resets the ball from the kitchen. Repeat, practicing the split-step timing as you advance.",
            SourceUrl = "https://pickleballkitchen.com/transition-zone/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000007"), Title = "Dink Patterns: Straight and Cross", Category = "Dinking", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "Partner drill: alternate between straight-ahead dinks and cross-court dinks in a structured pattern (2 straight, 2 cross). Helps develop shot placement awareness. Run for 5-minute sets.",
            SourceUrl = "https://pickleballkitchen.com/dinking-drills/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000008"), Title = "Backhand Reset Under Pressure", Category = "Resets", TargetDUPRLevel = 3.5m, EstimatedDurationMinutes = 10,
            Description = "Partner speeds up balls at your backhand from the kitchen. Your job is to absorb the pace and drop the ball softly into the kitchen — a reset. Focus on giving with the paddle on contact. 3 sets of 15.",
            SourceUrl = "https://3rdshotdrop.com/backhand-reset/" },

        // 4.0 — Advanced
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000009"), Title = "Speed-Up Attack and Counter", Category = "Attacking", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 10,
            Description = "Player A speeds up a ball at Player B's body. Player B counters back at Player A's feet. Rally continues until someone misses or resets. Develops quick reflexes and body-ball recognition. 5-minute continuous play.",
            SourceUrl = "https://pickleballmax.com/speed-up-drill/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000010"), Title = "Erne Setup and Execution", Category = "Volleys", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Practice the Erne: dink cross-court to pull opponent wide, then jump around the post or step through the kitchen corner to volley from outside the NVZ. Shadow practice 10 times then live reps with a feeding partner.",
            SourceUrl = "https://pickleballmax.com/erne-drill/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000011"), Title = "Advanced Third Shot Drop Sequences", Category = "Drops", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Baseline player hits third shot drops until one lands in the kitchen and is unattackable, then advances. Kitchen player applies pressure — speeds up anything above the net. Measures quality of drop under pressure. Competitive scoring.",
            SourceUrl = "https://pickleballkitchen.com/advanced-third-shot/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000012"), Title = "Competitive Dinking with Speed-Up Option", Category = "Dinking", TargetDUPRLevel = 4.0m, EstimatedDurationMinutes = 15,
            Description = "Cross-court dinking rally where either player can speed up at any time. The other player must counter or reset. Play to 11 points. Builds patience and the ability to recognize and create opportunities.",
            SourceUrl = "https://3rdshotdrop.com/competitive-dinking/" },

        // 5.0 — Professional
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000013"), Title = "ATP and Roll Volley Combination", Category = "Attacking", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 15,
            Description = "Advanced tournament drill: partner feeds wide balls to practice the Around-The-Post (ATP) shot followed immediately by a roll volley to the open court. Execute 20 reps each side with correct footwork and follow-through.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000014"), Title = "Full-Court Scenario Game Simulation", Category = "General", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 20,
            Description = "Professional-level competitive rally simulation: points start with a serve and play out at full pace. Emphasis on pattern play — third shot, advance, reset if needed, find and attack the first pop-up. Analyze patterns after each point. Best of 21.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000015"), Title = "High-Level Firefight Training", Category = "Volleys", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 10,
            Description = "Both players at kitchen line engaging in rapid-fire speed-up exchanges. No resets allowed — must counter or win the point. Trains competitive fast-hands reflexes at professional pace. 10-point games with full tracking.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
        new Drill { Id = Guid.Parse("a1000000-0000-0000-0000-000000000016"), Title = "Serve plus Tournament Pattern Play", Category = "Serving", TargetDUPRLevel = 5.0m, EstimatedDurationMinutes = 20,
            Description = "Professional serve targeting: practice power serves to T, body, and wide positions, each followed by a planned third shot pattern (drop vs. drive decision based on return depth). Full match-simulation pressure with scoring.",
            SourceUrl = "https://usapickleball.org/what-is-pickleball/official-rules/" },
    };
}
