using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallControl.Api.Domain;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

public sealed class QueueXapiClient : IQueueXapiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private const int ActiveCallsTopLimit = 100;
    private const int CallHistoryViewTopLimit = 100;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IQueueXapiAccessTokenProvider _tokenProvider;
    private readonly QueueXapiClientOptions _options;
    private readonly ILogger<QueueXapiClient> _logger;

    public QueueXapiClient(
        IHttpClientFactory httpClientFactory,
        IQueueXapiAccessTokenProvider tokenProvider,
        IOptions<QueueXapiClientOptions> options,
        ILogger<QueueXapiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Task<XapiODataCollectionResponse<XapiPbxQueueDto>> ListQueuesAsync(QueueODataQuery query, CancellationToken ct)
        => GetODataCollectionAsync<XapiPbxQueueDto>("/Queues", query, ct);

    public async Task<XapiPbxQueueDto?> GetQueueAsync(
        int queueId,
        IEnumerable<string>? select,
        IEnumerable<string>? expand,
        CancellationToken ct)
    {
        var path = QueueXapiODataQueryBuilder.Append(
            $"/Queues({queueId})",
            QueueXapiODataQueryBuilder.BuildSelectExpand(select, expand));

        try
        {
            return await SendJsonAsync<XapiPbxQueueDto>(HttpMethod.Get, path, payload: null, ct);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task<XapiPbxQueueDto> CreateQueueAsync(XapiPbxQueueDto request, CancellationToken ct)
    {
        try
        {
            return await SendJsonAsync<XapiPbxQueueDto>(HttpMethod.Post, "/Queues", request, ct);
        }
        catch (BadRequestException ex) when (ShouldRetryWithDeltaEnvelope(ex))
        {
            _logger.LogInformation(
                "3CX XAPI /Queues POST rejected raw payload with a delta validation error; retrying with delta wrapper.");
            return await SendJsonAsync<XapiPbxQueueDto>(
                HttpMethod.Post,
                "/Queues",
                new XapiDeltaEnvelope<XapiPbxQueueDto>(request),
                ct);
        }
    }

    public async Task UpdateQueueAsync(int queueId, XapiPbxQueueDto request, CancellationToken ct)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Patch, $"/Queues({queueId})", request, ifMatch: null, ct);
        }
        catch (BadRequestException ex) when (ShouldRetryWithDeltaEnvelope(ex))
        {
            _logger.LogInformation(
                "3CX XAPI /Queues PATCH rejected raw payload with a delta validation error; retrying with delta wrapper.");
            await SendNoContentAsync(
                HttpMethod.Patch,
                $"/Queues({queueId})",
                new XapiDeltaEnvelope<XapiPbxQueueDto>(request),
                ifMatch: null,
                ct);
        }
    }

    public Task DeleteQueueAsync(int queueId, string? ifMatch, CancellationToken ct)
        => SendNoContentAsync(HttpMethod.Delete, $"/Queues({queueId})", payload: null, ifMatch, ct);

    public Task<XapiODataCollectionResponse<XapiPbxQueueAgentDto>> ListQueueAgentsAsync(int queueId, QueueODataQuery query, CancellationToken ct)
        => GetODataCollectionAsync<XapiPbxQueueAgentDto>($"/Queues({queueId})/Agents", query, ct);

    public Task<XapiODataCollectionResponse<XapiPbxQueueManagerDto>> ListQueueManagersAsync(int queueId, QueueODataQuery query, CancellationToken ct)
        => GetODataCollectionAsync<XapiPbxQueueManagerDto>($"/Queues({queueId})/Managers", query, ct);

    public Task ResetQueueStatisticsAsync(int queueId, CancellationToken ct)
        => SendNoContentAsync(HttpMethod.Post, $"/Queues({queueId})/Pbx.ResetQueueStatistics", payload: null, ifMatch: null, ct);

    public Task<XapiODataCollectionResponse<XapiPbxActiveCallDto>> ListActiveCallsAsync(QueueODataQuery query, CancellationToken ct)
    {
        query ??= new QueueODataQuery();

        var requestedTop = query.Top;
        var clampedTop = requestedTop.HasValue
            ? Math.Clamp(requestedTop.Value, 1, ActiveCallsTopLimit)
            : (int?)null;

        if (requestedTop.HasValue && requestedTop.Value != clampedTop)
        {
            _logger.LogWarning(
                "Clamping /ActiveCalls $top from {RequestedTop} to {ClampedTop} to satisfy 3CX XAPI limit.",
                requestedTop.Value,
                clampedTop);
        }

        var sanitizedQuery = new QueueODataQuery
        {
            Top = clampedTop,
            Skip = query.Skip,
            Search = query.Search,
            Filter = query.Filter,
            Count = query.Count,
            OrderBy = query.OrderBy?.ToList() ?? [],
            Select = query.Select?.ToList() ?? [],
            Expand = query.Expand?.ToList() ?? []
        };

        return GetODataCollectionAsync<XapiPbxActiveCallDto>("/ActiveCalls", sanitizedQuery, ct);
    }

    public Task<XapiODataCollectionResponse<XapiPbxCallHistoryViewDto>> ListCallHistoryViewAsync(QueueODataQuery query, CancellationToken ct)
    {
        query ??= new QueueODataQuery();

        var requestedTop = query.Top;
        var clampedTop = requestedTop.HasValue
            ? Math.Clamp(requestedTop.Value, 1, CallHistoryViewTopLimit)
            : (int?)null;

        if (requestedTop.HasValue && requestedTop.Value != clampedTop)
        {
            _logger.LogWarning(
                "Clamping /CallHistoryView $top from {RequestedTop} to {ClampedTop} to improve 3CX XAPI compatibility.",
                requestedTop.Value,
                clampedTop);
        }

        var sanitizedQuery = new QueueODataQuery
        {
            Top = clampedTop,
            Skip = query.Skip,
            Search = query.Search,
            Filter = query.Filter,
            Count = query.Count,
            OrderBy = query.OrderBy?.ToList() ?? [],
            Select = query.Select?.ToList() ?? [],
            Expand = query.Expand?.ToList() ?? []
        };

        return GetODataCollectionAsync<XapiPbxCallHistoryViewDto>("/CallHistoryView", sanitizedQuery, ct);
    }

    public Task<XapiODataCollectionResponse<XapiPbxCallLogDataDto>> GetCallLogDataAsync(
        string relativeFunctionPath,
        QueueODataQuery query,
        CancellationToken ct)
    {
        var validatedPath = ValidateDocumentedCallLogPath(relativeFunctionPath);
        return GetODataCollectionAsync<XapiPbxCallLogDataDto>(validatedPath, query, ct);
    }

    private async Task<XapiODataCollectionResponse<TItem>> GetODataCollectionAsync<TItem>(
        string endpointPath,
        QueueODataQuery query,
        CancellationToken ct)
    {
        var path = QueueXapiODataQueryBuilder.Append(endpointPath, QueueXapiODataQueryBuilder.Build(query));
        var result = await SendJsonAsync<XapiODataCollectionResponse<TItem>>(HttpMethod.Get, path, payload: null, ct);
        result.Value ??= [];
        return result;
    }

    private async Task<TResponse> SendJsonAsync<TResponse>(
        HttpMethod method,
        string endpointPath,
        object? payload,
        CancellationToken ct)
    {
        using var request = BuildRequest(method, endpointPath, payload, ifMatch: null);
        using var response = await SendAuthorizedWithSingle401RetryAsync(request, endpointPath, ct);
        var body = await ReadAsStringAsync(response, ct);

        QueueXapiErrorParser.ThrowIfError(response.StatusCode, body, BuildXapiPath(endpointPath));

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new UpstreamApiException(
                $"3CX XAPI returned an empty JSON body for {BuildXapiPath(endpointPath)}.",
                (int)response.StatusCode);
        }

        try
        {
            var model = JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
            if (model is null)
            {
                throw new UpstreamApiException(
                    $"3CX XAPI returned a null JSON body for {BuildXapiPath(endpointPath)}.",
                    (int)response.StatusCode);
            }

            return model;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize 3CX XAPI response for {Endpoint}.", endpointPath);
            throw new UpstreamApiException(
                $"Failed to deserialize 3CX XAPI response for {BuildXapiPath(endpointPath)}.",
                (int)response.StatusCode);
        }
    }

    private async Task SendNoContentAsync(
        HttpMethod method,
        string endpointPath,
        object? payload,
        string? ifMatch,
        CancellationToken ct)
    {
        using var request = BuildRequest(method, endpointPath, payload, ifMatch);
        using var response = await SendAuthorizedWithSingle401RetryAsync(request, endpointPath, ct);
        var body = await ReadAsStringAsync(response, ct);
        QueueXapiErrorParser.ThrowIfError(response.StatusCode, body, BuildXapiPath(endpointPath));
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string endpointPath, object? payload, string? ifMatch)
    {
        var request = new HttpRequestMessage(method, BuildXapiPath(endpointPath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAuthorizedWithSingle401RetryAsync(
        HttpRequestMessage request,
        string endpointPath,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(QueueXapiHttpClientNames.Api);
        client.BaseAddress ??= _options.GetAuthorityBaseUri();

        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _tokenProvider.InvalidateCachedToken();
        _logger.LogWarning("Queue XAPI returned 401 for {Endpoint}; retrying once with refreshed token.", endpointPath);

        var retryRequest = await CloneRequestAsync(request, ct);
        token = await _tokenProvider.GetAccessTokenAsync(ct);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private string BuildXapiPath(string endpointPath)
    {
        var value = endpointPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException("XAPI endpoint path is required.");
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("QueueXapiClient expects relative XAPI paths.");
        }

        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            value = "/" + value;
        }

        var basePath = _options.GetNormalizedXapiBasePath();
        if (value.Equals(basePath, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return $"{basePath}{value}";
    }

    private static string ValidateDocumentedCallLogPath(string relativeFunctionPath)
    {
        var value = (relativeFunctionPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException("A documented /ReportCallLogData function path is required.");
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Call log function path must be relative to the XAPI service.");
        }

        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            value = "/" + value;
        }

        if (!value.StartsWith("/ReportCallLogData/", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(
                "Only documented /ReportCallLogData function endpoints are supported by GetCallLogDataAsync.");
        }

        return value;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken ct)
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

        var bytes = await request.Content.ReadAsByteArrayAsync(ct);
        var content = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Content = content;
        return clone;
    }

    private static async Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        return response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(ct);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private static bool ShouldRetryWithDeltaEnvelope(BadRequestException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("delta", StringComparison.OrdinalIgnoreCase)
            && message.Contains("required", StringComparison.OrdinalIgnoreCase)
            && message.Contains("/Queues", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class XapiDeltaEnvelope<T>
    {
        public XapiDeltaEnvelope(T delta)
        {
            Delta = delta;
        }

        [JsonPropertyName("delta")]
        public T Delta { get; }
    }

}
