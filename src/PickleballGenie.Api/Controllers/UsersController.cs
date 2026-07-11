using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PickleballGenie.Data;
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
    private static readonly string[] AllowedAvatarContentTypes = { "image/jpeg", "image/png" };

    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IDuprService _duprService;
    private readonly AppDbContext _context;
    private readonly IGoogleTokenValidator _googleTokenValidator;
    private readonly IAppleTokenValidator _appleTokenValidator;

    public UsersController(
        UserManager<User> userManager,
        IConfiguration configuration,
        IDuprService duprService,
        AppDbContext context,
        IGoogleTokenValidator googleTokenValidator,
        IAppleTokenValidator appleTokenValidator)
    {
        _userManager = userManager;
        _configuration = configuration;
        _duprService = duprService;
        _context = context;
        _googleTokenValidator = googleTokenValidator;
        _appleTokenValidator = appleTokenValidator;
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
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
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
            return Ok(new { Token = IssueJwt(user), IsProfileComplete = user.IsProfileComplete });
        }

        return Unauthorized(new { Message = "Invalid credentials" });
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.IdToken))
            return BadRequest(new { Message = "idToken is required." });

        SocialUserInfo info;
        try
        {
            info = await _googleTokenValidator.ValidateAsync(request.IdToken);
        }
        catch (SocialTokenValidationException ex)
        {
            return Unauthorized(new { Message = ex.Message });
        }

        return await ExternalLoginAsync("Google", info);
    }

    [HttpPost("apple-login")]
    public async Task<IActionResult> AppleLogin([FromBody] AppleLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.IdentityToken))
            return BadRequest(new { Message = "identityToken is required." });

        SocialUserInfo info;
        try
        {
            info = await _appleTokenValidator.ValidateAsync(request.IdentityToken);
        }
        catch (SocialTokenValidationException ex)
        {
            return Unauthorized(new { Message = ex.Message });
        }

        // Apple only sends the user's name on the first authorization, via the client.
        info.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();
        info.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();

        return await ExternalLoginAsync("Apple", info);
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        return Ok(await BuildUserResponseAsync(user));
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        if (request.ZipCode != null)
        {
            var zip = request.ZipCode.Trim();
            if (zip.Length != 5 || !zip.All(char.IsAsciiDigit))
                return BadRequest(new { Message = "Zip code must be 5 digits." });
            user.ZipCode = zip;
        }

        if (request.FirstName != null) user.FirstName = request.FirstName.Trim();
        if (request.LastName != null) user.LastName = request.LastName.Trim();
        if (request.HomeCityId.HasValue) user.HomeCityId = request.HomeCityId;
        if (request.HomeCityName != null) user.HomeCityName = request.HomeCityName.Trim();
        if (request.DominantHand != null) user.DominantHand = request.DominantHand.Trim().ToLowerInvariant();
        if (request.YearsPlaying.HasValue) user.YearsPlaying = request.YearsPlaying;
        if (request.PreferredPlayStyle != null) user.PreferredPlayStyle = request.PreferredPlayStyle.Trim().ToLowerInvariant();
        if (request.AvatarId != null) user.AvatarId = request.AvatarId.Trim();
        if (request.PreferredSessionDurationMinutes.HasValue) user.PreferredSessionDurationMinutes = request.PreferredSessionDurationMinutes;

        if (request.SinglesDUPR.HasValue || request.DoublesDUPR.HasValue)
        {
            if (user.IsDuprLinked)
                return BadRequest(new { Message = "Cannot manually update ratings for a linked DUPR account." });

            if (request.SinglesDUPR.HasValue) user.SinglesDUPR = request.SinglesDUPR;
            if (request.DoublesDUPR.HasValue) user.DoublesDUPR = request.DoublesDUPR;
        }

        if (request.TargetDUPR.HasValue)
        {
            if (request.TargetDUPR.Value < user.CurrentDUPR)
                return BadRequest(new { Message = "Target DUPR must be greater than or equal to your current DUPR." });
            user.TargetDUPR = request.TargetDUPR.Value;
        }
        else if (user.TargetDUPR < user.CurrentDUPR)
        {
            // Keep the invariant intact when only current ratings moved up.
            user.TargetDUPR = user.CurrentDUPR;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(await BuildUserResponseAsync(user));
    }

    [Authorize]
    [HttpPut("profile/ratings")]
    public async Task<IActionResult> UpdateRatings([FromBody] UpdateRatingsRequest request)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        if (user.IsDuprLinked)
        {
            // DUPR-linked accounts cannot override their official ratings, but can set their target
            if (request.SinglesDUPR.HasValue || request.DoublesDUPR.HasValue)
                return BadRequest(new { Message = "Cannot manually update ratings for a linked DUPR account." });
        }
        else
        {
            user.SinglesDUPR = request.SinglesDUPR;
            user.DoublesDUPR = request.DoublesDUPR;
        }

        if (request.TargetDUPR.HasValue)
        {
            var currentDUPR = Math.Max(user.SinglesDUPR ?? 0m, user.DoublesDUPR ?? 0m);
            if (request.TargetDUPR.Value < currentDUPR)
                return BadRequest(new { Message = "Target DUPR must be greater than or equal to your current DUPR." });
            user.TargetDUPR = request.TargetDUPR.Value;
        }

        await _userManager.UpdateAsync(user);

        return Ok(await BuildUserResponseAsync(user));
    }

    [Authorize]
    [HttpPost("profile/avatar")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> UploadAvatar()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        var contentType = Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (contentType == null || !AllowedAvatarContentTypes.Contains(contentType))
            return BadRequest(new { Message = "Avatar must be uploaded as image/jpeg or image/png." });

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        var data = buffer.ToArray();
        if (data.Length == 0)
            return BadRequest(new { Message = "Avatar image body is empty." });

        var existing = await _context.UserAvatarImages.FindAsync(user.Id);
        if (existing == null)
        {
            _context.UserAvatarImages.Add(new UserAvatarImage
            {
                UserId = user.Id,
                Data = data,
                ContentType = contentType,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Data = data;
            existing.ContentType = contentType;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        user.AvatarId = "custom";
        await _userManager.UpdateAsync(user);

        return Ok(await BuildUserResponseAsync(user));
    }

    [Authorize]
    [HttpGet("profile/avatar")]
    public async Task<IActionResult> GetAvatar()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        var avatar = await _context.UserAvatarImages.FindAsync(user.Id);
        if (avatar == null)
            return NotFound();

        return File(avatar.Data, avatar.ContentType);
    }

    [Authorize]
    [HttpDelete("profile/avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized();

        var avatar = await _context.UserAvatarImages.FindAsync(user.Id);
        if (avatar != null)
        {
            _context.UserAvatarImages.Remove(avatar);
            await _context.SaveChangesAsync();
        }

        if (user.AvatarId == "custom")
        {
            user.AvatarId = null;
            await _userManager.UpdateAsync(user);
        }

        return Ok(await BuildUserResponseAsync(user));
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

            return Ok(new { Token = IssueJwt(user), IsProfileComplete = user.IsProfileComplete });
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

    private async Task<IActionResult> ExternalLoginAsync(string provider, SocialUserInfo info)
    {
        var user = await _userManager.FindByLoginAsync(provider, info.Subject);
        if (user == null)
        {
            user = await _userManager.FindByEmailAsync(info.Email);
            if (user == null)
            {
                user = new User
                {
                    UserName = info.Email,
                    Email = info.Email,
                    EmailConfirmed = true,
                    FirstName = info.FirstName,
                    LastName = info.LastName
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return BadRequest(createResult.Errors);
            }
            else
            {
                // Link the social identity to the existing email account,
                // filling in names if we didn't have them yet.
                var changed = false;
                if (string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(info.FirstName))
                {
                    user.FirstName = info.FirstName;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(user.LastName) && !string.IsNullOrWhiteSpace(info.LastName))
                {
                    user.LastName = info.LastName;
                    changed = true;
                }
                if (changed)
                    await _userManager.UpdateAsync(user);
            }

            var addLoginResult = await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, info.Subject, provider));
            if (!addLoginResult.Succeeded)
                return BadRequest(addLoginResult.Errors);
        }

        return Ok(new { Token = IssueJwt(user), IsProfileComplete = user.IsProfileComplete });
    }

    private async Task<User?> GetAuthenticatedUserAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return null;

        return await _userManager.FindByIdAsync(userId.ToString());
    }

    private string IssueJwt(User user)
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
        return tokenHandler.WriteToken(token);
    }

    private async Task<UserResponse> BuildUserResponseAsync(User user)
    {
        var hasCustomAvatar = await _context.UserAvatarImages.AnyAsync(a => a.UserId == user.Id);

        return new UserResponse
        {
            Id = user.Id.ToString(),
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ZipCode = user.ZipCode,
            HomeCityId = user.HomeCityId,
            HomeCityName = user.HomeCityName,
            DominantHand = user.DominantHand,
            YearsPlaying = user.YearsPlaying,
            PreferredPlayStyle = user.PreferredPlayStyle,
            AvatarId = user.AvatarId,
            SinglesDUPR = user.SinglesDUPR,
            DoublesDUPR = user.DoublesDUPR,
            TargetDUPR = user.TargetDUPR,
            IsDuprLinked = user.IsDuprLinked,
            PreferredSessionDurationMinutes = user.PreferredSessionDurationMinutes,
            IsProfileComplete = user.IsProfileComplete,
            HasCustomAvatar = hasCustomAvatar
        };
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
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

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
}

public class AppleLoginRequest
{
    public string IdentityToken { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
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

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ZipCode { get; set; }
    public int? HomeCityId { get; set; }
    public string? HomeCityName { get; set; }
    public string? DominantHand { get; set; }
    public int? YearsPlaying { get; set; }
    public string? PreferredPlayStyle { get; set; }
    public string? AvatarId { get; set; }
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal? TargetDUPR { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
}

public class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ZipCode { get; set; }
    public int? HomeCityId { get; set; }
    public string? HomeCityName { get; set; }
    public string? DominantHand { get; set; }
    public int? YearsPlaying { get; set; }
    public string? PreferredPlayStyle { get; set; }
    public string? AvatarId { get; set; }
    public decimal? SinglesDUPR { get; set; }
    public decimal? DoublesDUPR { get; set; }
    public decimal TargetDUPR { get; set; }
    public bool IsDuprLinked { get; set; }
    public int? PreferredSessionDurationMinutes { get; set; }
    public bool IsProfileComplete { get; set; }
    public bool HasCustomAvatar { get; set; }
}
