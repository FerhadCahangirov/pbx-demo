using System.Text;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

public static class QueueXapiODataQueryBuilder
{
    public static string FormatDateTimeOffsetUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O");

    public static string EscapeODataStringLiteral(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    public static string Build(QueueODataQuery? query)
    {
        if (query is null)
        {
            return string.Empty;
        }

        var parameters = new List<KeyValuePair<string, string>>();

        AddInt(parameters, "$top", query.Top);
        AddInt(parameters, "$skip", query.Skip);
        AddString(parameters, "$search", query.Search);
        AddString(parameters, "$filter", query.Filter);
        AddBool(parameters, "$count", query.Count);
        AddCsv(parameters, "$orderby", query.OrderBy);
        AddCsv(parameters, "$select", query.Select);
        AddCsv(parameters, "$expand", query.Expand);

        return ToQueryString(parameters);
    }

    public static string BuildSelectExpand(IEnumerable<string>? select, IEnumerable<string>? expand)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        AddCsv(parameters, "$select", select);
        AddCsv(parameters, "$expand", expand);
        return ToQueryString(parameters);
    }

    public static string Append(string path, string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return path;
        }

        return path.Contains('?', StringComparison.Ordinal)
            ? $"{path}&{queryString.TrimStart('?')}"
            : $"{path}{queryString}";
    }

    private static void AddInt(List<KeyValuePair<string, string>> parameters, string key, int? value)
    {
        if (value is > -1)
        {
            parameters.Add(new KeyValuePair<string, string>(key, value.Value.ToString()));
        }
    }

    private static void AddBool(List<KeyValuePair<string, string>> parameters, string key, bool? value)
    {
        if (value.HasValue)
        {
            parameters.Add(new KeyValuePair<string, string>(key, value.Value ? "true" : "false"));
        }
    }

    private static void AddString(List<KeyValuePair<string, string>> parameters, string key, string? value)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            parameters.Add(new KeyValuePair<string, string>(key, trimmed));
        }
    }

    private static void AddCsv(List<KeyValuePair<string, string>> parameters, string key, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        var items = values
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (items.Length == 0)
        {
            return;
        }

        parameters.Add(new KeyValuePair<string, string>(key, string.Join(",", items!)));
    }

    private static string ToQueryString(IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("?");
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(parameters[i].Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameters[i].Value));
        }

        return builder.ToString();
    }
}
