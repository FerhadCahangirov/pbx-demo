using CallControl.Api.Domain;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

public sealed class QueueXapiClientOptions
{
    public string PbxBase { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string XapiBasePath { get; set; } = "/xapi/v1";
    public string TokenPath { get; set; } = "/connect/token";
    public int TimeoutSeconds { get; set; } = 30;
    public int TokenExpirySkewSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 250;
    public int CircuitBreakerFailureCount { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public Uri GetAuthorityBaseUri()
    {
        return NormalizeAuthorityUri(PbxBase);
    }

    public string GetNormalizedXapiBasePath()
    {
        return NormalizeAbsolutePath(XapiBasePath, "/xapi/v1", trimTrailingSlash: true);
    }

    public string GetNormalizedTokenPath()
    {
        return NormalizeAbsolutePath(TokenPath, "/connect/token", trimTrailingSlash: false);
    }

    public void EnsureCredentialsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InternalServerErrorException("3CX XAPI client credentials are not configured.");
        }
    }

    public static Uri NormalizeAuthorityUri(string? rawBase)
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

    public static string NormalizeAbsolutePath(string? rawPath, string fallback, bool trimTrailingSlash)
    {
        var value = string.IsNullOrWhiteSpace(rawPath) ? fallback : rawPath.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            value = "/" + value;
        }

        while (value.Contains("//", StringComparison.Ordinal))
        {
            value = value.Replace("//", "/", StringComparison.Ordinal);
        }

        if (trimTrailingSlash && value.Length > 1)
        {
            value = value.TrimEnd('/');
        }

        return value;
    }
}

internal static class QueueXapiHttpClientNames
{
    public const string Api = "QueueXapiApiClient";
    public const string Token = "QueueXapiTokenClient";
}
