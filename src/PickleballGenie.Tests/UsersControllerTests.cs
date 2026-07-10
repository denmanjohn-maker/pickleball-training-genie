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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
        var config = GetMockConfiguration();
        var fakeDuprService = new FakeDuprService();
        var controller = new UsersController(userManager, config, fakeDuprService);

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
