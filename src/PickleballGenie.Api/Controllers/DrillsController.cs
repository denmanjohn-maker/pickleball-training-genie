using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Data;
using PickleballGenie.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace PickleballGenie.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DrillsController : ControllerBase
{
    private readonly AppDbContext _context;

    public DrillsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetDrills([FromQuery] string? category, [FromQuery] decimal? level)
    {
        var query = _context.Drills.AsQueryable();

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(d => d.Category == category);
        }

        if (level.HasValue)
        {
            query = query.Where(d => d.TargetDUPRLevel == level.Value);
        }

        return Ok(await query.ToListAsync());
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _context.Users.FindAsync(Guid.Parse(userId));
        if (user == null)
            return NotFound("User not found");

        // Logic Example: Recommend drills designed for CurrentDUPR and CurrentDUPR + 0.5 (TargetDUPR approach)
        var recommendedDrills = await _context.Drills
            .Where(d => d.TargetDUPRLevel >= user.CurrentDUPR && d.TargetDUPRLevel <= user.TargetDUPR)
            .ToListAsync();

        return Ok(recommendedDrills);
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteDrill(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var drill = await _context.Drills.FindAsync(id);
        if (drill == null)
            return NotFound("Drill not found");

        var progress = await _context.UserDrillProgresses
            .FirstOrDefaultAsync(p => p.UserId == Guid.Parse(userId) && p.DrillId == id);

        if (progress == null)
        {
            progress = new UserDrillProgress
            {
                UserId = Guid.Parse(userId),
                DrillId = id,
                Status = DrillStatus.Mastered,
                CompletedAt = DateTime.UtcNow
            };
            _context.UserDrillProgresses.Add(progress);
        }
        else
        {
            progress.Status = DrillStatus.Mastered;
            progress.CompletedAt = DateTime.UtcNow;
            _context.UserDrillProgresses.Update(progress);
        }

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Drill marked as mastered" });
    }
}
