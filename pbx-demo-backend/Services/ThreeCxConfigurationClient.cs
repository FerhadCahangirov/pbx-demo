using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class ThreeCxConfigurationClient
{
    private readonly SoftphoneOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ThreeCxConfigurationClient> _logger;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly Uri _baseUri;

    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc;

    public ThreeCxConfigurationClient(
        IOptions<SoftphoneOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ThreeCxConfigurationClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUri = NormalizeBaseUri(_options.ThreeCx.PbxBase);
    }

    public Task<JsonElement> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        return SendJsonAsync(HttpMethod.Get, relativePath, null, cancellationToken);
    }

    public Task<JsonElement> PostJsonAsync(string relativePath, object? payload, CancellationToken cancellationToken)
    {
        return SendJsonAsync(HttpMethod.Post, relativePath, payload, cancellationToken);
    }

    public Task<JsonElement> PatchJsonAsync(string relativePath, object? payload, CancellationToken cancellationToken)
    {
        return SendJsonAsync(HttpMethod.Patch, relativePath, payload, cancellationToken);
    }

    public Task SendPatchNoContentAsync(string relativePath, object? payload, CancellationToken cancellationToken)
    {
        return SendNoContentAsync(HttpMethod.Patch, relativePath, payload, cancellationToken);
    }

    public Task SendPostNoContentAsync(string relativePath, object? payload, CancellationToken cancellationToken)
    {
        return SendNoContentAsync(HttpMethod.Post, relativePath, payload, cancellationToken);
    }

    public Task SendDeleteNoContentAsync(string relativePath, CancellationToken cancellationToken)
    {
        return SendNoContentAsync(HttpMethod.Delete, relativePath, null, cancellationToken);
    }

    public async Task<(JsonElement Body, string Version)> GetVersionProbeAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        var body = await ReadBodyAsStringAsync(response, cancellationToken);
        EnsureSucceeded(response.StatusCode, body);

        var version = response.Headers.TryGetValues("X-3CX-Version", out var values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;

        return (DeserializeToElementOrEmpty(body), version);
    }

    private async Task<JsonElement> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        var body = await ReadBodyAsStringAsync(response, cancellationToken);
        EnsureSucceeded(response.StatusCode, body);
        return DeserializeToElementOrEmpty(body);
    }

    private async Task SendNoContentAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        var body = await ReadBodyAsStringAsync(response, cancellationToken);
        EnsureSucceeded(response.StatusCode, body);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(ThreeCxConfigurationClient));
        client.BaseAddress = _baseUri;

        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        InvalidateToken();

        var retryRequest = await CloneRequestAsync(request, cancellationToken);
        token = await GetAccessTokenAsync(cancellationToken);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(retryRequest, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken)
                && _expiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _accessToken;
            }

            var appId = _options.ThreeCx.AppId?.Trim() ?? string.Empty;
            var appSecret = _options.ThreeCx.AppSecret?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            {
                throw new InternalServerErrorException("3CX app credentials are not configured.");
            }

            var client = _httpClientFactory.CreateClient("ThreeCxConfigurationTokenClient");
            client.BaseAddress = _baseUri;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = appId,
                ["client_secret"] = appSecret,
                ["grant_type"] = "client_credentials"
            });

            using var response = await client.PostAsync("/connect/token", content, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new UnauthorizedException(
                    $"3CX token request failed: {(int)response.StatusCode} {response.StatusCode}. {payload}");
            }

            using var document = JsonDocument.Parse(payload);
            var accessToken = document.RootElement.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InternalServerErrorException("3CX token response does not contain access_token.");
            }

            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
                && expiresElement.TryGetInt32(out var seconds)
                ? Math.Max(60, seconds)
                : 3600;

            _accessToken = accessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _expiresAtUtc = DateTimeOffset.MinValue;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is null)
        {
            return clone;
        }

        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var content = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Content = content;
        return clone;
    }

    private static async Task<string> ReadBodyAsStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static JsonElement DeserializeToElementOrEmpty(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            using var emptyDocument = JsonDocument.Parse("{}");
            return emptyDocument.RootElement.Clone();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallbackDocument = JsonDocument.Parse(JsonSerializer.Serialize(new { raw = payload }));
            return fallbackDocument.RootElement.Clone();
        }
    }

    private static void EnsureSucceeded(HttpStatusCode statusCode, string payload)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        var message = ExtractErrorMessage(payload);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"3CX API returned {(int)statusCode} {statusCode}.";
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException(message);
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedException(message);
        }

        if ((int)statusCode is >= 400 and <= 499)
        {
            throw new BadRequestException(message);
        }

        throw new InternalServerErrorException(message);
    }

    private static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("error_description", out var errorDescription)
                && errorDescription.ValueKind == JsonValueKind.String)
            {
                return errorDescription.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nestedMessage)
                    && nestedMessage.ValueKind == JsonValueKind.String)
                {
                    return nestedMessage.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            return payload;
        }

        return payload;
    }

    private static Uri NormalizeBaseUri(string? rawBase)
    {
        var value = rawBase?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InternalServerErrorException("3CX base URL is not configured.");
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InternalServerErrorException("3CX base URL is invalid.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InternalServerErrorException("3CX base URL must use HTTP or HTTPS.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }
}
