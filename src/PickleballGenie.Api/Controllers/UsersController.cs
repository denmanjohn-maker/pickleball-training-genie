using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PickleballGenie.Models;
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

    public UsersController(UserManager<User> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            CurrentDUPR = request.CurrentDUPR,
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
                    new Claim(ClaimTypes.Email, user.Email!),
                    new Claim("CurrentDUPR", user.CurrentDUPR.ToString())
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return Ok(new { Token = tokenHandler.WriteToken(token) });
        }

        return Unauthorized(new { Message = "Invalid credentials" });
    }

    [HttpPut("{id}/dupr")]
    public async Task<IActionResult> UpdateDupr(Guid id, [FromBody] UpdateDuprRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            return NotFound();

        user.CurrentDUPR = request.CurrentDUPR;
        user.TargetDUPR = request.TargetDUPR;

        await _userManager.UpdateAsync(user);

        return Ok(new { Message = "DUPR updated successfully" });
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public decimal CurrentDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UpdateDuprRequest
{
    public decimal CurrentDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
}
