using System.Text.Json;

namespace CallControl.Api.Infrastructure;

public static class RequestInputResolver
{
    private const string RawBodyCacheKey = "__request_raw_body_cache";
    private const string JsonBodyCacheKey = "__request_json_body_cache";
    private const string JsonBodyParsedKey = "__request_json_body_parsed";

    public static async Task<string?> ResolveFieldAsync(
        HttpRequest request,
        string fieldName,
        CancellationToken cancellationToken,
        bool allowRawFallback = false)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        if (request.Query.TryGetValue(fieldName, out var queryValues))
        {
            var queryValue = queryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(queryValue))
            {
                return queryValue.Trim();
            }
        }

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var formValue = form[fieldName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(formValue))
            {
                return formValue.Trim();
            }
        }

        var jsonRoot = await GetJsonRootAsync(request, cancellationToken);
        if (jsonRoot.HasValue)
        {
            if (jsonRoot.Value.ValueKind == JsonValueKind.Object
                && TryGetPropertyCaseInsensitive(jsonRoot.Value, fieldName, out var field))
            {
                return field.ValueKind switch
                {
                    JsonValueKind.String => field.GetString()?.Trim(),
                    JsonValueKind.Number => field.ToString().Trim(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => null
                };
            }

            if (allowRawFallback && jsonRoot.Value.ValueKind == JsonValueKind.String)
            {
                return jsonRoot.Value.GetString()?.Trim();
            }
        }

        if (!allowRawFallback)
        {
            return null;
        }

        var rawBody = await GetRawBodyAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        return rawBody.Trim().Trim('"');
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string propertyName, out JsonElement propertyValue)
    {
        if (obj.TryGetProperty(propertyName, out propertyValue))
        {
            return true;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = property.Value;
                return true;
            }
        }

        propertyValue = default;
        return false;
    }

    private static async Task<JsonElement?> GetJsonRootAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var items = request.HttpContext.Items;
        if (items.TryGetValue(JsonBodyParsedKey, out var parsedFlag) && parsedFlag is true)
        {
            if (items.TryGetValue(JsonBodyCacheKey, out var cachedRoot) && cachedRoot is JsonElement rootElement)
            {
                return rootElement;
            }

            return null;
        }

        items[JsonBodyParsedKey] = true;
        var rawBody = await GetRawBodyAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var cloned = document.RootElement.Clone();
            items[JsonBodyCacheKey] = cloned;
            return cloned;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> GetRawBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var items = request.HttpContext.Items;
        if (items.TryGetValue(RawBodyCacheKey, out var cachedRaw))
        {
            return cachedRaw as string;
        }

        if (request.ContentLength is null or <= 0)
        {
            items[RawBodyCacheKey] = null!;
            return null;
        }

        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        items[RawBodyCacheKey] = rawBody;
        return rawBody;
    }
}
