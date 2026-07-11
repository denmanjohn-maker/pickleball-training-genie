using Google.Apis.Auth;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PickleballGenie.Api.Services;

public class SocialUserInfo
{
    public string Subject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class SocialTokenValidationException : Exception
{
    public SocialTokenValidationException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IGoogleTokenValidator
{
    Task<SocialUserInfo> ValidateAsync(string idToken);
}

public class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly IConfiguration _configuration;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<SocialUserInfo> ValidateAsync(string idToken)
    {
        var clientId = _configuration["Google:ClientId"] ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        if (string.IsNullOrEmpty(clientId))
            throw new SocialTokenValidationException("Google Sign-In is not configured on the server (Google:ClientId missing).");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            });
        }
        catch (InvalidJwtException ex)
        {
            throw new SocialTokenValidationException("Invalid Google ID token.", ex);
        }

        if (string.IsNullOrEmpty(payload.Email))
            throw new SocialTokenValidationException("Google account did not provide an email address.");

        return new SocialUserInfo
        {
            Subject = payload.Subject,
            Email = payload.Email,
            FirstName = payload.GivenName,
            LastName = payload.FamilyName
        };
    }
}

public interface IAppleTokenValidator
{
    Task<SocialUserInfo> ValidateAsync(string identityToken);
}

public class AppleTokenValidator : IAppleTokenValidator
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string AppleKeysUrl = "https://appleid.apple.com/auth/keys";
    private static readonly TimeSpan KeyCacheDuration = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static JsonWebKeySet? _cachedKeySet;
    private static DateTime _keySetFetchedAt = DateTime.MinValue;
    private static readonly SemaphoreSlim _keyLock = new(1, 1);

    public AppleTokenValidator(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<SocialUserInfo> ValidateAsync(string identityToken)
    {
        var bundleId = _configuration["Apple:BundleId"] ?? Environment.GetEnvironmentVariable("APPLE_BUNDLE_ID");
        if (string.IsNullOrEmpty(bundleId))
            throw new SocialTokenValidationException("Sign in with Apple is not configured on the server (Apple:BundleId missing).");

        var keySet = await GetAppleKeySetAsync();

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidAudience = bundleId,
            IssuerSigningKeys = keySet.Keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(identityToken, parameters, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            throw new SocialTokenValidationException("Invalid Apple identity token.", ex);
        }

        var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrEmpty(subject))
            throw new SocialTokenValidationException("Apple identity token did not contain a subject.");
        if (string.IsNullOrEmpty(email))
            throw new SocialTokenValidationException("Apple identity token did not contain an email address.");

        return new SocialUserInfo { Subject = subject, Email = email };
    }

    private async Task<JsonWebKeySet> GetAppleKeySetAsync()
    {
        if (_cachedKeySet != null && DateTime.UtcNow - _keySetFetchedAt < KeyCacheDuration)
            return _cachedKeySet;

        await _keyLock.WaitAsync();
        try
        {
            if (_cachedKeySet != null && DateTime.UtcNow - _keySetFetchedAt < KeyCacheDuration)
                return _cachedKeySet;

            var json = await _httpClient.GetStringAsync(AppleKeysUrl);
            _cachedKeySet = new JsonWebKeySet(json);
            _keySetFetchedAt = DateTime.UtcNow;
            return _cachedKeySet;
        }
        catch (HttpRequestException ex)
        {
            if (_cachedKeySet != null)
                return _cachedKeySet; // stale keys beat no keys
            throw new SocialTokenValidationException("Could not fetch Apple public keys.", ex);
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
