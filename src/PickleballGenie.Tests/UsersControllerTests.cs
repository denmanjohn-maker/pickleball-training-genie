using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickleballGenie.Api.Controllers;
using PickleballGenie.Api.Services;
using PickleballGenie.Data;
using PickleballGenie.Models;
using Xunit;

namespace PickleballGenie.Tests;

public class UsersControllerTests
{
    private DbContextOptions<AppDbContext> GetInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private (UserManager<User> userManager, AppDbContext context) GetUserManagerAndContext()
    {
        var options = GetInMemoryOptions();
        var context = new AppDbContext(options);

        var userStore = new UserStore<User, IdentityRole<Guid>, AppDbContext, Guid>(context);

        var optionsAccessor = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        var passwordHasher = new PasswordHasher<User>();
        var userValidators = new List<IUserValidator<User>> { new UserValidator<User>() };
        var passwordValidators = new List<IPasswordValidator<User>> { new PasswordValidator<User>() };
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errors = new IdentityErrorDescriber();
        var logger = new Logger<UserManager<User>>(new LoggerFactory());

        var userManager = new UserManager<User>(
            userStore,
            optionsAccessor,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            null!,
            logger
        );

        return (userManager, context);
    }

    private UsersController CreateController(
        UserManager<User> userManager,
        AppDbContext context,
        FakeDuprService? duprService = null,
        FakeGoogleTokenValidator? googleValidator = null,
        FakeAppleTokenValidator? appleValidator = null)
    {
        return new UsersController(
            userManager,
            GetMockConfiguration(),
            duprService ?? new FakeDuprService(),
            context,
            googleValidator ?? new FakeGoogleTokenValidator(),
            appleValidator ?? new FakeAppleTokenValidator());
    }

    private static void AuthenticateAs(UsersController controller, Guid userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    private IConfiguration GetMockConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string?> {
            {"JwtSecret", "MyVerySecretTestJwtSecretKeyForUnitTestPurpleElephant123!"},
            {"Dupr:ClientId", "test-client-id"},
            {"Dupr:ClientSecret", "test-client-secret"},
            {"Dupr:RedirectUri", "http://localhost/callback"}
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task Register_WithValidTargetDUPR_Succeeds()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var request = new RegisterRequest
        {
            Email = "valid@example.com",
            Password = "SecurePassword123!",
            SinglesDUPR = 3.5m,
            DoublesDUPR = 4.0m,
            TargetDUPR = 4.0m
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var user = await userManager.FindByEmailAsync("valid@example.com");
        Assert.NotNull(user);
        Assert.Equal(3.5m, user.SinglesDUPR);
        Assert.Equal(4.0m, user.DoublesDUPR);
        Assert.Equal(4.0m, user.TargetDUPR);
    }

    [Fact]
    public async Task Register_WithTargetDUPR_LessThanSinglesDUPR_ReturnsBadRequest()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var request = new RegisterRequest
        {
            Email = "invalid@example.com",
            Password = "SecurePassword123!",
            SinglesDUPR = 4.5m,
            DoublesDUPR = 4.0m,
            TargetDUPR = 4.2m
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Target DUPR must be greater than or equal to", badRequestResult.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task Register_WithTargetDUPR_LessThanDoublesDUPR_ReturnsBadRequest()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var request = new RegisterRequest
        {
            Email = "invalid2@example.com",
            Password = "SecurePassword123!",
            SinglesDUPR = 3.5m,
            DoublesDUPR = 4.2m,
            TargetDUPR = 4.0m
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Target DUPR must be greater than or equal to", badRequestResult.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task UpdateRatings_UpdatesRatings_WhenNotDuprLinked()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "profile@example.com",
            Email = "profile@example.com",
            SinglesDUPR = 3.0m,
            DoublesDUPR = 3.0m,
            TargetDUPR = 4.5m,
            IsDuprLinked = false
        };
        await userManager.CreateAsync(user, "SecurePassword123!");

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };

        var request = new UpdateRatingsRequest
        {
            SinglesDUPR = 3.5m,
            DoublesDUPR = 3.8m
        };

        // Act
        var result = await controller.UpdateRatings(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal(3.5m, response.SinglesDUPR);
        Assert.Equal(3.8m, response.DoublesDUPR);

        var updatedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(updatedUser);
        Assert.Equal(3.5m, updatedUser.SinglesDUPR);
        Assert.Equal(3.8m, updatedUser.DoublesDUPR);
    }

    [Fact]
    public async Task UpdateRatings_Fails_WhenDuprLinked()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "profile_linked@example.com",
            Email = "profile_linked@example.com",
            SinglesDUPR = 3.0m,
            DoublesDUPR = 3.0m,
            TargetDUPR = 4.5m,
            IsDuprLinked = true
        };
        await userManager.CreateAsync(user, "SecurePassword123!");

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };

        var request = new UpdateRatingsRequest
        {
            SinglesDUPR = 3.5m,
            DoublesDUPR = 3.8m
        };

        // Act
        var result = await controller.UpdateRatings(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Cannot manually update ratings", badRequestResult.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task DuprLogin_NewUser_CreatesAndLinksUser()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        fakeDuprService.ExchangeCallback = (code) => Task.FromResult(new DuprProfileDto
        {
            Email = "new_dupr_user@example.com",
            AccountId = "dupr_999",
            SinglesRating = 4.25m,
            DoublesRating = 4.50m
        });

        var request = new DuprLoginRequest { AuthCode = "valid_code" };

        // Act
        var result = await controller.DuprLogin(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var user = await userManager.FindByEmailAsync("new_dupr_user@example.com");
        Assert.NotNull(user);
        Assert.True(user.IsDuprLinked);
        Assert.Equal("dupr_999", user.DuprAccountId);
        Assert.Equal(4.25m, user.SinglesDUPR);
        Assert.Equal(4.50m, user.DoublesDUPR);
        Assert.Equal(5.00m, user.TargetDUPR); // Math.Max(4.25, 4.50) + 0.5 = 5.00
    }

    [Fact]
    public async Task DuprLogin_ExistingUser_UpdatesAndLinksUser()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var existingUser = new User
        {
            UserName = "existing_dupr_user@example.com",
            Email = "existing_dupr_user@example.com",
            SinglesDUPR = 3.0m,
            DoublesDUPR = 3.0m,
            TargetDUPR = 4.0m,
            IsDuprLinked = false
        };
        await userManager.CreateAsync(existingUser, "SecurePassword123!");

        fakeDuprService.ExchangeCallback = (code) => Task.FromResult(new DuprProfileDto
        {
            Email = "existing_dupr_user@example.com",
            AccountId = "dupr_888",
            SinglesRating = 4.10m,
            DoublesRating = 4.15m
        });

        var request = new DuprLoginRequest { AuthCode = "valid_code" };

        // Act
        var result = await controller.DuprLogin(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var user = await userManager.FindByEmailAsync("existing_dupr_user@example.com");
        Assert.NotNull(user);
        Assert.True(user.IsDuprLinked);
        Assert.Equal("dupr_888", user.DuprAccountId);
        Assert.Equal(4.10m, user.SinglesDUPR);
        Assert.Equal(4.15m, user.DoublesDUPR);
        Assert.Equal(4.00m, user.TargetDUPR); // Target DUPR is not overwritten for existing users
    }

    [Fact]
    public async Task UpdateProfile_CompletesProfile_AndFlipsIsProfileComplete()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "onboard@example.com",
            Email = "onboard@example.com"
        };
        await userManager.CreateAsync(user, "SecurePassword123!");
        AuthenticateAs(controller, userId);

        var request = new UpdateProfileRequest
        {
            FirstName = "Sam",
            LastName = "Player",
            ZipCode = "75201",
            HomeCityId = 12,
            HomeCityName = "Dallas, TX",
            DominantHand = "Right",
            YearsPlaying = 3,
            PreferredPlayStyle = "Doubles",
            AvatarId = "builtin:paddle",
            SinglesDUPR = 3.5m,
            DoublesDUPR = 4.0m,
            TargetDUPR = 4.5m,
            PreferredSessionDurationMinutes = 45
        };

        // Act
        var result = await controller.UpdateProfile(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal("Sam", response.FirstName);
        Assert.Equal("Player", response.LastName);
        Assert.Equal("75201", response.ZipCode);
        Assert.Equal(12, response.HomeCityId);
        Assert.Equal("Dallas, TX", response.HomeCityName);
        Assert.Equal("right", response.DominantHand);
        Assert.Equal("doubles", response.PreferredPlayStyle);
        Assert.Equal("builtin:paddle", response.AvatarId);
        Assert.True(response.IsProfileComplete);
        Assert.False(response.HasCustomAvatar);
    }

    [Fact]
    public async Task UpdateProfile_RejectsInvalidZipCode()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "zip@example.com",
            Email = "zip@example.com"
        };
        await userManager.CreateAsync(user, "SecurePassword123!");
        AuthenticateAs(controller, userId);

        // Act
        var result = await controller.UpdateProfile(new UpdateProfileRequest { ZipCode = "1234" });

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Zip code", badRequest.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task UpdateProfile_RejectsTargetBelowCurrentDUPR()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var fakeDuprService = new FakeDuprService();
        var controller = CreateController(userManager, context, fakeDuprService);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "target@example.com",
            Email = "target@example.com"
        };
        await userManager.CreateAsync(user, "SecurePassword123!");
        AuthenticateAs(controller, userId);

        // Act
        var result = await controller.UpdateProfile(new UpdateProfileRequest
        {
            SinglesDUPR = 4.0m,
            DoublesDUPR = 4.0m,
            TargetDUPR = 3.5m
        });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GoogleLogin_NewUser_CreatesUserWithIncompleteProfile()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var googleValidator = new FakeGoogleTokenValidator
        {
            Result = new SocialUserInfo
            {
                Subject = "google-sub-123",
                Email = "google_user@example.com",
                FirstName = "Gina",
                LastName = "Google"
            }
        };
        var controller = CreateController(userManager, context, googleValidator: googleValidator);

        // Act
        var result = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "fake-token" });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var user = await userManager.FindByEmailAsync("google_user@example.com");
        Assert.NotNull(user);
        Assert.Equal("Gina", user.FirstName);
        Assert.Equal("Google", user.LastName);
        Assert.False(user.IsProfileComplete);

        var logins = await userManager.GetLoginsAsync(user);
        Assert.Contains(logins, l => l.LoginProvider == "Google" && l.ProviderKey == "google-sub-123");
    }

    [Fact]
    public async Task GoogleLogin_ExistingEmailUser_LinksLoginInsteadOfDuplicating()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var existing = new User
        {
            UserName = "linked@example.com",
            Email = "linked@example.com",
            SinglesDUPR = 3.5m,
            DoublesDUPR = 3.5m,
            TargetDUPR = 4.0m
        };
        await userManager.CreateAsync(existing, "SecurePassword123!");

        var googleValidator = new FakeGoogleTokenValidator
        {
            Result = new SocialUserInfo
            {
                Subject = "google-sub-456",
                Email = "linked@example.com",
                FirstName = "Lin",
                LastName = "Ked"
            }
        };
        var controller = CreateController(userManager, context, googleValidator: googleValidator);

        // Act — twice, second call exercises the FindByLoginAsync fast path
        var first = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "fake-token" });
        var second = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "fake-token" });

        // Assert
        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(second);
        Assert.Single(userManager.Users.Where(u => u.Email == "linked@example.com"));

        var user = await userManager.FindByEmailAsync("linked@example.com");
        Assert.NotNull(user);
        Assert.Equal("Lin", user.FirstName); // name backfilled from Google
        Assert.Equal(3.5m, user.SinglesDUPR); // existing data untouched
    }

    [Fact]
    public async Task AppleLogin_InvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var controller = CreateController(userManager, context, appleValidator: new FakeAppleTokenValidator());

        // Act
        var result = await controller.AppleLogin(new AppleLoginRequest { IdentityToken = "garbage" });

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task DeleteAccount_RemovesUser_AndReturnsNoContent()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var controller = CreateController(userManager, context);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "delete_me@example.com",
            Email = "delete_me@example.com",
            TargetDUPR = 4.0m
        };
        await userManager.CreateAsync(user, "SecurePassword123!");
        AuthenticateAs(controller, userId);

        // Act
        var result = await controller.DeleteAccount();

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Null(await userManager.FindByIdAsync(userId.ToString()));
    }

    [Fact]
    public async Task DeleteAccount_ReturnsUnauthorized_WhenNoAuthenticatedUser()
    {
        // Arrange
        var (userManager, context) = GetUserManagerAndContext();
        var controller = CreateController(userManager, context);
        // No NameIdentifier claim -> no authenticated user.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Act
        var result = await controller.DeleteAccount();

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }
}

public class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public SocialUserInfo? Result { get; set; }

    public Task<SocialUserInfo> ValidateAsync(string idToken)
    {
        if (Result == null)
            throw new SocialTokenValidationException("Invalid Google ID token.");
        return Task.FromResult(Result);
    }
}

public class FakeAppleTokenValidator : IAppleTokenValidator
{
    public SocialUserInfo? Result { get; set; }

    public Task<SocialUserInfo> ValidateAsync(string identityToken)
    {
        if (Result == null)
            throw new SocialTokenValidationException("Invalid Apple identity token.");
        return Task.FromResult(Result);
    }
}

public class FakeDuprService : IDuprService
{
    public Func<string, Task<DuprProfileDto>>? ExchangeCallback { get; set; }

    public Task<DuprProfileDto> ExchangeCodeAndFetchProfileAsync(string authCode)
    {
        if (ExchangeCallback != null)
        {
            return ExchangeCallback(authCode);
        }

        return Task.FromResult(new DuprProfileDto
        {
            Email = "dupr_user@example.com",
            AccountId = "dupr_123",
            SinglesRating = 4.25m,
            DoublesRating = 4.50m
        });
    }
}
