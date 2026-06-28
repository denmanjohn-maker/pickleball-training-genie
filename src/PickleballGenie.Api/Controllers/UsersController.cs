using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PickleballGenie.Models;
using PickleballGenie.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PickleballGenie.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IDuprService _duprService;

    public UsersController(UserManager<User> userManager, IConfiguration configuration, IDuprService duprService)
    {
        _userManager = userManager;
        _configuration = configuration;
        _duprService = duprService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        decimal? maxCurrent = null;
        if (request.SinglesDUPR.HasValue) maxCurrent = request.SinglesDUPR.Value;
        if (request.DoublesDUPR.HasValue) maxCurrent = maxCurrent.HasValue ? Math.Max(maxCurrent.Value, request.DoublesDUPR.Value) : request.DoublesDUPR.Value;

        if (maxCurrent.HasValue && request.TargetDUPR < maxCurrent.Value)
        {
            return BadRequest(new { Message = "Target DUPR must be greater than or equal to the maximum of your Singles and Doubles DUPR." });
        }

        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            SinglesDUPR = request.SinglesDUPR,
            DoublesDUPR = request.DoublesDUPR,
            TargetDUPR = request.TargetDUPR,
            PreferredSessionDurationMinutes = request.PreferredSessionDurationMinutes
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            return Ok(new { Message = "User registered successfully" });
        }

        return BadRequest(result.Errors);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user != null && await _userManager.CheckPasswordAsync(user, request.Password))
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSecret = _configuration["JwtSecret"] ?? Environment.GetEnvironmentVariable("JWT_SECRET") ?? "A_Super_Secret_Key_For_Development_Only_Do_Not_Use_In_Prod_Please_Change_This_To_A_Secure_Key_That_Is_Long_Enough!";
            var key = Encoding.ASCII.GetBytes(jwtSecret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email!)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return Ok(new { Token = tokenHandler.WriteToken(token) });
        }

        return Unauthorized(new { Message = "Invalid credentials" });
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return NotFound();

        return Ok(new UserResponse
        {
            Id = user.Id.ToString(),
            Email = user.Email!,
            SinglesDUPR = user.SinglesDUPR,
            DoublesDUPR = user.DoublesDUPR,
            TargetDUPR = user.TargetDUPR,
            IsDuprLinked = user.IsDuprLinked,
            PreferredSessionDurationMinutes = user.PreferredSessionDurationMinutes
        });
    }

    [Authorize]
    [HttpPut("profile/ratings")]
    public async Task<IActionResult> UpdateRatings([FromBody] UpdateRatingsRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return NotFound();

        // Disallow manual update if DUPR is officially linked
        if (user.IsDuprLinked)
            return BadRequest(new { Message = "Cannot manually update ratings for a linked DUPR account." });

        user.SinglesDUPR = request.SinglesDUPR;
        user.DoublesDUPR = request.DoublesDUPR;

        if (request.TargetDUPR.HasValue)
        {
            var newCurrentDUPR = Math.Max(request.SinglesDUPR ?? 0m, request.DoublesDUPR ?? 0m);
            if (request.TargetDUPR.Value < newCurrentDUPR)
                return BadRequest(new { Message = "Target DUPR must be greater than or equal to your current DUPR." });
            user.TargetDUPR = request.TargetDUPR.Value;
        }

        await _userManager.UpdateAsync(user);

        return Ok(new UserResponse
        {
            Id = user.Id.ToString(),
            Email = user.Email!,
            SinglesDUPR = user.SinglesDUPR,
            DoublesDUPR = user.DoublesDUPR,
            TargetDUPR = user.TargetDUPR,
            IsDuprLinked = user.IsDuprLinked,
            PreferredSessionDurationMinutes = user.PreferredSessionDurationMinutes
        });
    }

    [HttpPost("dupr-login")]
    public async Task<IActionResult> DuprLogin([FromBody] DuprLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.AuthCode))
        {
            return BadRequest(new { Message = "Auth code is required." });
        }

        try
        {
            var duprProfile = await _duprService.ExchangeCodeAndFetchProfileAsync(request.AuthCode);

            var user = await _userManager.FindByEmailAsync(duprProfile.Email);
            if (user == null)
            {
                var singles = duprProfile.SinglesRating;
                var doubles = duprProfile.DoublesRating;
                var calculatedTarget = Math.Max(singles ?? 0.0m, doubles ?? 0.0m) + 0.5m;
                if (calculatedTarget <= 0.5m)
                {
                    calculatedTarget = 5.0m;
                }

                user = new User
                {
                    UserName = duprProfile.Email,
                    Email = duprProfile.Email,
                    SinglesDUPR = singles,
                    DoublesDUPR = doubles,
                    TargetDUPR = calculatedTarget,
                    IsDuprLinked = true,
                    DuprAccountId = duprProfile.AccountId
                };

                // Create user with a random secure password since they login via DUPR
                var createResult = await _userManager.CreateAsync(user, Guid.NewGuid().ToString() + "A1!");
                if (!createResult.Succeeded)
                {
                    return BadRequest(createResult.Errors);
                }
            }
            else
            {
                user.SinglesDUPR = duprProfile.SinglesRating;
                user.DoublesDUPR = duprProfile.DoublesRating;
                user.IsDuprLinked = true;
                user.DuprAccountId = duprProfile.AccountId;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return BadRequest(updateResult.Errors);
                }
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSecret = _configuration["JwtSecret"] ?? Environment.GetEnvironmentVariable("JWT_SECRET") ?? "A_Super_Secret_Key_For_Development_Only_Do_Not_Use_In_Prod_Please_Change_This_To_A_Secure_Key_That_Is_Long_Enough!";
            var key = Encoding.ASCII.GetBytes(jwtSecret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email!)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return Ok(new { Token = tokenHandler.WriteToken(token) });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { Message = "DUPR Authentication failed: " + ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "An unexpected error occurred during DUPR login.", Details = ex.Message });
        }
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class DuprLoginRequest
{
    public string AuthCode { get; set; } = string.Empty;
}

public class UpdateRatingsRequest
{
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal? TargetDUPR { get; set; }
}

public class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public bool IsDuprLinked { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
}
