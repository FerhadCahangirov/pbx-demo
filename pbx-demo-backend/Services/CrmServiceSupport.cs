using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallControl.Api.Domain;

namespace CallControl.Api.Services;

internal static class CrmServiceSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static readonly HashSet<string> AllowedRoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system_owners",
        "system_admins",
        "group_owners",
        "managers",
        "group_admins",
        "receptionists",
        "users"
    };

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static string NormalizeOrDefault(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public static string NormalizeRoleName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!AllowedRoleNames.Contains(normalized))
        {
            throw new BadRequestException($"Role '{normalized}' is not valid for 3CX.");
        }

        return normalized;
    }

    public static void ValidateDepartmentRoles(IReadOnlyList<CrmUserDepartmentRoleRequest> roles)
    {
        foreach (var role in roles)
        {
            if (role.AppDepartmentId <= 0)
            {
                throw new BadRequestException("AppDepartmentId must be greater than zero.");
            }

            NormalizeRoleName(role.RoleName);
        }
    }

    public static string SerializeAsJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static T DeserializeOrDefault<T>(string? value, T fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static IEnumerable<JsonElement> GetValueArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().ToList();
    }

    public static int? GetInt32(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static string? GetString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    public static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$#%*";
        var bytes = RandomNumberGenerator.GetBytes(16);
        var result = new StringBuilder(16);
        foreach (var value in bytes)
        {
            result.Append(chars[value % chars.Length]);
        }

        return result.ToString();
    }
}
