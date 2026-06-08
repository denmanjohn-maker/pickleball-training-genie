using Microsoft.EntityFrameworkCore;
using PickleballGenie.Models;

namespace PickleballGenie.Data;

public static class DbSeeder
{
    public static async Task SeedDrillsAsync(AppDbContext dbContext)
    {
        if (await dbContext.Drills.AnyAsync())
            return;

        var drills = GetCuratedDrills();
        dbContext.Drills.AddRange(drills);
        await dbContext.SaveChangesAsync();
    }

    private static List<Drill> GetCuratedDrills() => new()
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
