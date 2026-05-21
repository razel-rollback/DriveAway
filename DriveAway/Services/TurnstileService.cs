using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace DriveAway.Services
{
    public interface ITurnstileService
    {
        Task<bool> VerifyTokenAsync(string token);
    }

    public class TurnstileService : ITurnstileService
    {
        private readonly HttpClient _httpClient;
        private readonly TurnstileOptions _options;
        private readonly ILogger<TurnstileService> _logger;
        private readonly IWebHostEnvironment _env;

        public TurnstileService(HttpClient httpClient, IOptions<TurnstileOptions> options, ILogger<TurnstileService> logger, IWebHostEnvironment env)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
            _env = env;
        }

        public async Task<bool> VerifyTokenAsync(string token)
        {
            if (_env.IsDevelopment())
            {
                _logger.LogInformation("Turnstile verification bypassed in Development environment.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Turnstile validation failed: Token is empty.");
                return false;
            }

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("secret", _options.SecretKey),
                    new KeyValuePair<string, string>("response", token)
                });

                var response = await _httpClient.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                var turnstileResponse = JsonSerializer.Deserialize<TurnstileResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (turnstileResponse?.Success == true)
                {
                    return true;
                }
                
                _logger.LogWarning("Turnstile validation failed. Error codes: {ErrorCodes}", string.Join(", ", turnstileResponse?.ErrorCodes ?? Array.Empty<string>()));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during Turnstile validation.");
                return false;
            }
        }

        private class TurnstileResponse
        {
            public bool Success { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("error-codes")]
            public string[]? ErrorCodes { get; set; }
        }
    }
}
