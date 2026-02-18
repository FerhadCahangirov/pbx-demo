using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CallControl.Api.Domain;

namespace CallControl.Api.Services;

public sealed class ThreeCxCallControlClient : IAsyncDisposable
{
    private readonly ThreeCxConnectSettings _settings;
    private readonly int _maxReconnectAttempts;
    private readonly TimeSpan _reconnectDelay;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ThreeCxCallControlClient> _logger;
    private readonly SemaphoreSlim _socketGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Uri _baseUri;
    private readonly Uri _webSocketUri;

    private HttpClient? _apiClient;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _webSocketCts;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;
    private int _reconnectLoopStarted;
    private bool _disposed;
    private bool _manualDisconnect;

    public event Func<ThreeCxWsEvent, Task>? WsEventReceived;
    public event Func<bool, Task>? WsConnectionStateChanged;

    public bool IsWebSocketConnected => _webSocket is { State: WebSocketState.Open };

    public ThreeCxCallControlClient(
        ThreeCxConnectSettings settings,
        int maxReconnectAttempts,
        TimeSpan reconnectDelay,
        IHttpClientFactory httpClientFactory,
        ILogger<ThreeCxCallControlClient> logger)
    {
        _settings = settings;
        _maxReconnectAttempts = Math.Max(1, maxReconnectAttempts);
        _reconnectDelay = reconnectDelay;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _baseUri = NormalizeBaseUri(settings.PbxBase);
        var wsScheme = _baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var wsPortPart = _baseUri.IsDefaultPort ? string.Empty : $":{_baseUri.Port}";
        _webSocketUri = new Uri($"{wsScheme}://{_baseUri.Host}{wsPortPart}/callcontrol/ws");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _apiClient = _httpClientFactory.CreateClient(nameof(ThreeCxCallControlClient));
        _apiClient.BaseAddress = _baseUri;
        _manualDisconnect = false;

        await ConnectWebSocketAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ThreeCxDnInfo>> GetFullInfoAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/callcontrol");
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return DeserializeOrDefault<List<ThreeCxDnInfo>>(body) ?? [];
    }

    public async Task<ThreeCxCallControlResult?> MakeCallAsync(string sourceDn, string destination, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new BadRequestException("Destination is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/callcontrol/{sourceDn}/makecall")
        {
            Content = JsonContent.Create(new { destination = destination.Trim() })
        };

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadCallControlResultAsync(response, cancellationToken);
    }

    public async Task<ThreeCxCallControlResult?> MakeCallFromDeviceAsync(
        string sourceDn,
        string encodedDeviceId,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new BadRequestException("Destination is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/callcontrol/{sourceDn}/devices/{encodedDeviceId}/makecall")
        {
            Content = JsonContent.Create(new { destination = destination.Trim() })
        };

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadCallControlResultAsync(response, cancellationToken);
    }

    public async Task<ThreeCxCallControlResult?> ControlParticipantAsync(
        string sourceDn,
        long participantId,
        string action,
        string? destination,
        CancellationToken cancellationToken = default)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("ParticipantId must be a positive number.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new BadRequestException("Action is required.");
        }

        HttpContent content = string.IsNullOrWhiteSpace(destination)
            ? JsonContent.Create(new Dictionary<string, string>())
            : JsonContent.Create(new { destination = destination.Trim() });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/callcontrol/{sourceDn}/participants/{participantId}/{action.Trim()}")
        {
            Content = content
        };

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadCallControlResultAsync(response, cancellationToken);
    }

    public async Task<JsonElement?> RequestEntityAsync(string entityPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityPath))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, entityPath);
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manualDisconnect = true;
        await CloseWebSocketAsync();
        _apiClient?.Dispose();
        _socketGate.Dispose();
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var bearerToken = await GetBearerTokenAsync(cancellationToken);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearerToken);

        var response = await (_apiClient?.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            ?? throw new InternalServerErrorException("3CX HTTP client is not initialized."));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _accessToken = null;
            var retryRequest = await CloneRequestAsync(request, cancellationToken);
            bearerToken = await GetBearerTokenAsync(cancellationToken);
            retryRequest.Headers.Authorization = AuthenticationHeaderValue.Parse(bearerToken);
            return await (_apiClient?.SendAsync(retryRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
                ?? throw new InternalServerErrorException("3CX HTTP client is not initialized."));
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken;
        }

        using var tokenClient = _httpClientFactory.CreateClient("ThreeCxTokenClient");
        tokenClient.BaseAddress = _baseUri;

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _settings.AppId,
            ["client_secret"] = _settings.AppSecret,
            ["grant_type"] = "client_credentials"
        });

        using var response = await tokenClient.PostAsync("/connect/token", content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedException($"3CX token request failed: {(int)response.StatusCode} {response.StatusCode} {payload}");
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            throw new InternalServerErrorException("3CX token response does not contain access_token.");
        }

        var accessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InternalServerErrorException("3CX token response returned an empty access_token.");
        }

        var expiresInSeconds = 3600;
        if (document.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var expiresValue))
        {
            expiresInSeconds = Math.Max(60, expiresValue);
        }

        _accessToken = $"Bearer {accessToken}";
        _accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        return _accessToken;
    }

    private async Task ConnectWebSocketAsync(CancellationToken cancellationToken)
    {
        await _socketGate.WaitAsync(cancellationToken);
        try
        {
            await CloseWebSocketUnsafeAsync();

            var bearerToken = await GetBearerTokenAsync(cancellationToken);
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            socket.Options.SetRequestHeader("Authorization", bearerToken);
            await socket.ConnectAsync(_webSocketUri, cancellationToken);

            _webSocket = socket;
            _webSocketCts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoopAsync(socket, _webSocketCts.Token), _webSocketCts.Token);
            await NotifyWsStateAsync(true);

            _logger.LogInformation("3CX WebSocket connected: {WebSocketUri}", _webSocketUri);
        }
        finally
        {
            _socketGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var payload = Encoding.UTF8.GetString(stream.ToArray());
                stream.SetLength(0);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                ThreeCxWsEvent? wsEvent;
                try
                {
                    wsEvent = JsonSerializer.Deserialize<ThreeCxWsEvent>(payload, _jsonOptions);
                }
                catch (JsonException)
                {
                    wsEvent = null;
                }

                if (wsEvent?.Event is not null && WsEventReceived is not null)
                {
                    await WsEventReceived.Invoke(wsEvent);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "3CX WebSocket receive loop failed.");
        }
        finally
        {
            await NotifyWsStateAsync(false);
            if (!_manualDisconnect && !_disposed)
            {
                StartReconnectLoop();
            }
        }
    }

    private void StartReconnectLoop()
    {
        if (Interlocked.CompareExchange(ref _reconnectLoopStarted, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 1; attempt <= _maxReconnectAttempts && !_disposed && !_manualDisconnect; attempt++)
                {
                    try
                    {
                        _logger.LogWarning("Trying to reconnect 3CX WebSocket. Attempt {Attempt}/{MaxAttempts}", attempt, _maxReconnectAttempts);
                        await Task.Delay(_reconnectDelay, CancellationToken.None);
                        await ConnectWebSocketAsync(CancellationToken.None);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "3CX WebSocket reconnect attempt {Attempt} failed.", attempt);
                    }
                }

                _logger.LogError("3CX WebSocket reconnection failed after {MaxAttempts} attempts.", _maxReconnectAttempts);
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectLoopStarted, 0);
            }
        });
    }

    private async Task NotifyWsStateAsync(bool connected)
    {
        if (WsConnectionStateChanged is not null)
        {
            await WsConnectionStateChanged.Invoke(connected);
        }
    }

    private async Task<ThreeCxCallControlResult?> ReadCallControlResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureCallControlResponseSucceeded(response.StatusCode, body);
        try
        {
            return DeserializeOrDefault<ThreeCxCallControlResult>(body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize 3CX call control result payload: {Payload}", body);
            return null;
        }
    }

    private static void EnsureCallControlResponseSucceeded(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        var message = BuildErrorMessage(statusCode, body);
        if (statusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException(message);
        }

        if ((int)statusCode is >= 400 and <= 499)
        {
            throw new UpstreamApiException(message, (int)statusCode);
        }

        throw new InternalServerErrorException(message);
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"3CX call control returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            var result = JsonSerializer.Deserialize<ThreeCxCallControlResult>(body);
            if (!string.IsNullOrWhiteSpace(result?.ReasonText))
            {
                return result.ReasonText!;
            }

            if (!string.IsNullOrWhiteSpace(result?.Reason))
            {
                return result.Reason!;
            }
        }
        catch (JsonException)
        {
        }

        return $"3CX call control returned {(int)statusCode} {statusCode}: {body}";
    }

    private static T? DeserializeOrDefault<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private async Task CloseWebSocketAsync()
    {
        await _socketGate.WaitAsync();
        try
        {
            await CloseWebSocketUnsafeAsync();
        }
        finally
        {
            _socketGate.Release();
        }
    }

    private async Task CloseWebSocketUnsafeAsync()
    {
        try
        {
            _webSocketCts?.Cancel();

            if (_webSocket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
        }
        catch
        {
            // Socket can already be in terminal state here.
        }
        finally
        {
            _webSocket?.Dispose();
            _webSocket = null;
            _webSocketCts?.Dispose();
            _webSocketCts = null;
        }
    }

    private static Uri NormalizeBaseUri(string rawBase)
    {
        var value = rawBase?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException("PBX base URL is required.");
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new BadRequestException("PBX base URL is invalid.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BadRequestException("PBX base URL must use HTTP or HTTPS.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ThreeCxCallControlClient));
        }
    }
}
