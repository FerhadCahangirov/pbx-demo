using System.Net;
using System.Text.Json;
using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

public interface IQueueXapiAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
    void InvalidateCachedToken();
}

public sealed class QueueXapiAccessTokenProvider : IQueueXapiAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QueueXapiAccessTokenProvider> _logger;
    private readonly QueueXapiClientOptions _options;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc;

    public QueueXapiAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<QueueXapiClientOptions> options,
        ILogger<QueueXapiAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken)
                && _expiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(Math.Max(5, _options.TokenExpirySkewSeconds)))
            {
                return _accessToken;
            }

            _options.EnsureCredentialsConfigured();
            var authority = _options.GetAuthorityBaseUri();

            var client = _httpClientFactory.CreateClient(QueueXapiHttpClientNames.Token);
            client.BaseAddress ??= authority;

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.GetNormalizedTokenPath())
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId.Trim(),
                    ["client_secret"] = _options.ClientSecret.Trim(),
                    ["grant_type"] = "client_credentials"
                })
            };

            using var response = await client.SendAsync(request, ct);
            var payload = await ReadAsStringAsync(response, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedException(
                    $"3CX token request failed: {QueueXapiErrorParser.ExtractErrorMessage(payload)}");
            }

            QueueXapiErrorParser.ThrowIfError(response.StatusCode, payload, _options.GetNormalizedTokenPath());

            using var document = JsonDocument.Parse(payload);
            var accessToken = document.RootElement.TryGetProperty("access_token", out var tokenElement)
                && tokenElement.ValueKind == JsonValueKind.String
                ? tokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InternalServerErrorException("3CX token response does not contain access_token.");
            }

            var expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
                && expiresElement.TryGetInt32(out var seconds)
                ? Math.Max(60, seconds)
                : 3600;

            _accessToken = accessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

            _logger.LogDebug("Queue XAPI token acquired; expires at {ExpiresAtUtc}.", _expiresAtUtc);
            return _accessToken;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse 3CX XAPI token response.");
            throw new InternalServerErrorException("Failed to parse 3CX token response.");
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public void InvalidateCachedToken()
    {
        _accessToken = null;
        _expiresAtUtc = DateTimeOffset.MinValue;
    }

    private static async Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        return response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(ct);
    }
}
