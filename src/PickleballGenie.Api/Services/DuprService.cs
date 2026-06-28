using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PickleballGenie.Api.Services;

public interface IDuprService
{
    Task<DuprProfileDto> ExchangeCodeAndFetchProfileAsync(string authCode);
}

public class DuprProfileDto
{
    public string Email { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public decimal? SinglesRating { get; set; }
    public decimal? DoublesRating { get; set; }
}

public class DuprService : IDuprService
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly ILogger<DuprService> _logger;

    public DuprService(HttpClient httpClient, IConfiguration configuration, ILogger<DuprService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientId = configuration["Dupr:ClientId"] ?? "dummy-client-id";
        _clientSecret = configuration["Dupr:ClientSecret"] ?? "dummy-client-secret";
        _redirectUri = configuration["Dupr:RedirectUri"] ?? "dummy-redirect-uri";
    }

    public async Task<DuprProfileDto> ExchangeCodeAndFetchProfileAsync(string authCode)
    {
        try
        {
            // 1. Exchange auth code for access token
            var tokenPayload = new
            {
                grant_type = "authorization_code",
                code = authCode,
                client_id = _clientId,
                client_secret = _clientSecret,
                redirect_uri = _redirectUri
            };

            var tokenResponse = await _httpClient.PostAsJsonAsync("oauth/token", tokenPayload);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync();
                _logger.LogError("DUPR token exchange failed: {StatusCode} - {Error}", tokenResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to exchange auth code with DUPR: {tokenResponse.StatusCode}");
            }

            var tokenData = await tokenResponse.Content.ReadFromJsonAsync<DuprTokenResponse>();
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                throw new Exception("Invalid response from DUPR token endpoint.");
            }

            // 2. Fetch user profile using access token
            using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "v1/user/profile");
            profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var profileResponse = await _httpClient.SendAsync(profileRequest);
            if (!profileResponse.IsSuccessStatusCode)
            {
                var errorContent = await profileResponse.Content.ReadAsStringAsync();
                _logger.LogError("DUPR profile fetch failed: {StatusCode} - {Error}", profileResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to fetch DUPR user profile: {profileResponse.StatusCode}");
            }

            var profileData = await profileResponse.Content.ReadFromJsonAsync<DuprProfileResponse>();
            if (profileData == null || string.IsNullOrEmpty(profileData.Email))
            {
                throw new Exception("Invalid response from DUPR profile endpoint.");
            }

            return new DuprProfileDto
            {
                Email = profileData.Email,
                AccountId = profileData.Id ?? string.Empty,
                SinglesRating = profileData.SinglesRating,
                DoublesRating = profileData.DoublesRating
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during DUPR OAuth exchange.");
            throw;
        }
    }

    private class DuprTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }

    private class DuprProfileResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("singles_rating")]
        public decimal? SinglesRating { get; set; }

        [JsonPropertyName("doubles_rating")]
        public decimal? DoublesRating { get; set; }
    }
}
