using System.Net;
using System.Text.Json;
using CallControl.Api.Domain;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

internal static class QueueXapiErrorParser
{
    public static void ThrowIfError(HttpStatusCode statusCode, string payload, string requestPath)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        var message = ExtractErrorMessage(payload);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"3CX XAPI returned {(int)statusCode} {statusCode}.";
        }

        var finalMessage = $"{message} (endpoint: {requestPath})";

        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new UnauthorizedException(finalMessage);
            case HttpStatusCode.Forbidden:
                throw new ForbiddenException(finalMessage);
            case HttpStatusCode.NotFound:
                throw new NotFoundException(finalMessage);
        }

        if ((int)statusCode is >= 400 and <= 499)
        {
            throw new BadRequestException(finalMessage);
        }

        throw new UpstreamApiException(finalMessage, (int)statusCode);
    }

    public static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (TryGetString(root, "error_description", out var errorDescription))
            {
                return errorDescription;
            }

            if (TryGetString(root, "message", out var message))
            {
                return message;
            }

            if (TryGetString(root, "detail", out var detail))
            {
                return detail;
            }

            if (TryGetString(root, "title", out var title))
            {
                return title;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetString(error, "code", out var code)
                        && TryGetString(error, "message", out var nestedMessage))
                    {
                        return string.IsNullOrWhiteSpace(code) ? nestedMessage : $"{code}: {nestedMessage}";
                    }

                    if (error.TryGetProperty("message", out var messageNode))
                    {
                        var odataMessage = ExtractODataMessageNode(messageNode);
                        if (!string.IsNullOrWhiteSpace(odataMessage))
                        {
                            return odataMessage;
                        }
                    }
                }
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                return $"{property.Name}: {item.GetString()}";
                            }
                        }
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return $"{property.Name}: {property.Value.GetString()}";
                    }
                }
            }
        }
        catch (JsonException)
        {
            return payload;
        }

        return payload;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ExtractODataMessageNode(JsonElement messageNode)
    {
        if (messageNode.ValueKind == JsonValueKind.String)
        {
            return messageNode.GetString() ?? string.Empty;
        }

        if (messageNode.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(messageNode, "value", out var value))
            {
                return value;
            }

            if (TryGetString(messageNode, "message", out var nested))
            {
                return nested;
            }
        }

        return string.Empty;
    }
}
