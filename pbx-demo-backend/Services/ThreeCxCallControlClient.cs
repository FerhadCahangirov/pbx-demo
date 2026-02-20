using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CallControl.Api.Domain;

namespace CallControl.Api.Services;

public sealed class ThreeCxCallControlClient : IAsyncDisposable
{
    private const int DefaultActionTimeoutSeconds = 30;
    private const string DefaultDiversionReason = "None";

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

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/callcontrol/{Uri.EscapeDataString(sourceDn)}/makecall")
        {
            Content = JsonContent.Create(new
            {
                destination = destination.Trim(),
                timeout = DefaultActionTimeoutSeconds
            })
        };

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadCallControlResultAsync(response, cancellationToken);
    }

    public async Task<ThreeCxCallControlResult?> MakeCallFromDeviceAsync(
        string sourceDn,
        string deviceId,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new BadRequestException("Destination is required.");
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new BadRequestException("Device id is required.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/callcontrol/{Uri.EscapeDataString(sourceDn)}/devices/{Uri.EscapeDataString(deviceId)}/makecall")
        {
            Content = JsonContent.Create(new
            {
                destination = destination.Trim(),
                timeout = DefaultActionTimeoutSeconds
            })
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
        if (string.IsNullOrWhiteSpace(sourceDn))
        {
            throw new BadRequestException("Source DN is required.");
        }

        if (participantId <= 0)
        {
            throw new BadRequestException("ParticipantId must be a positive number.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new BadRequestException("Action is required.");
        }

        var normalizedSourceDn = sourceDn.Trim();
        var normalizedAction = action.Trim().ToLowerInvariant();
        var normalizedDestination = destination?.Trim();
        if (string.Equals(normalizedAction, CallControlConstants.ParticipantActionDivert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAction, CallControlConstants.ParticipantActionRouteTo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAction, CallControlConstants.ParticipantActionTransferTo, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(normalizedDestination))
            {
                throw new BadRequestException($"Destination is required for action '{normalizedAction}'.");
            }
        }

        try
        {
            return await SendParticipantControlAsync(
                normalizedSourceDn,
                participantId,
                normalizedAction,
                normalizedDestination,
                cancellationToken);
        }
        catch (UpstreamApiException ex)
            when (string.Equals(normalizedAction, CallControlConstants.ParticipantActionAnswer, StringComparison.Ordinal)
                  && (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity
                      || ex.ErrorCode == (int)HttpStatusCode.NotFound))
        {
            var fallbackCandidate = await ResolveAnswerFallbackCandidateAsync(
                normalizedSourceDn,
                participantId,
                cancellationToken);
            if (fallbackCandidate is null
                || (fallbackCandidate.ParticipantId == participantId
                    && string.Equals(fallbackCandidate.Dn, normalizedSourceDn, StringComparison.Ordinal)))
            {
                throw;
            }

            _logger.LogInformation(
                "Retrying answer with participant {FallbackParticipantId} on DN {FallbackDn} after {StatusCode} for requested participant {ParticipantId} on DN {SourceDn}.",
                fallbackCandidate.ParticipantId,
                fallbackCandidate.Dn,
                ex.ErrorCode,
                participantId,
                normalizedSourceDn);

            return await SendParticipantControlAsync(
                fallbackCandidate.Dn,
                fallbackCandidate.ParticipantId,
                normalizedAction,
                normalizedDestination,
                cancellationToken);
        }
    }

    private async Task<ThreeCxCallControlResult?> SendParticipantControlAsync(
        string sourceDn,
        long participantId,
        string action,
        string? destination,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/callcontrol/{Uri.EscapeDataString(sourceDn)}/participants/{participantId}/{Uri.EscapeDataString(action)}");

        request.Content = JsonContent.Create(BuildParticipantControlPayload(action, destination));

        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadCallControlResultAsync(response, cancellationToken);
    }

    private static object BuildParticipantControlPayload(string action, string? destination)
    {
        if (string.Equals(action, CallControlConstants.ParticipantActionDivert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, CallControlConstants.ParticipantActionRouteTo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, CallControlConstants.ParticipantActionTransferTo, StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                reason = DefaultDiversionReason,
                destination,
                timeout = DefaultActionTimeoutSeconds
            };
        }

        return new { };
    }

    private async Task<AnswerCandidateReference?> ResolveAnswerFallbackCandidateAsync(
        string sourceDn,
        long participantId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ThreeCxDnInfo> fullInfo;
        try
        {
            fullInfo = await GetFullInfoAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Unable to load 3CX topology for answer fallback. DN={SourceDn}, ParticipantId={ParticipantId}",
                sourceDn,
                participantId);
            return null;
        }

        var candidates = fullInfo
            .Where(info => !string.IsNullOrWhiteSpace(info.Dn))
            .SelectMany(info => (info.Participants ?? [])
                .Where(participant => participant.Id.HasValue)
                .Select(participant => new AnswerCandidateReference(
                    info.Dn!,
                    participant.Id!.Value,
                    participant.CallId,
                    participant.Status,
                    participant.DirectControl ?? false,
                    info.Type)))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var requestedCandidate = candidates
            .FirstOrDefault(candidate =>
                candidate.ParticipantId == participantId
                && string.Equals(candidate.Dn, sourceDn, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault(candidate => candidate.ParticipantId == participantId);
        if (requestedCandidate?.CallId is not long callId)
        {
            return null;
        }

        return candidates
            .Where(candidate => candidate.CallId == callId)
            .Where(IsLikelyAnswerableCandidate)
            .OrderByDescending(candidate => IsRingingStatus(candidate.Status))
            .ThenByDescending(candidate => candidate.DirectControl)
            .ThenByDescending(candidate => string.Equals(candidate.Dn, sourceDn, StringComparison.Ordinal))
            .ThenBy(candidate => candidate.ParticipantId)
            .FirstOrDefault(candidate =>
                candidate.ParticipantId != participantId
                || !string.Equals(candidate.Dn, sourceDn, StringComparison.Ordinal));
    }

    private static bool IsRingingStatus(string? status)
    {
        return string.Equals(status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyAnswerableCandidate(AnswerCandidateReference candidate)
    {
        if (candidate.DirectControl)
        {
            return true;
        }

        return string.Equals(candidate.DnType, CallControlConstants.RoutePointType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AnswerCandidateReference(
        string Dn,
        long ParticipantId,
        long? CallId,
        string? Status,
        bool DirectControl,
        string? DnType);

    public async Task<(Stream Stream, string ContentType)> OpenParticipantAudioStreamAsync(
        string sourceDn,
        long participantId,
        CancellationToken cancellationToken = default)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("ParticipantId must be a positive number.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/callcontrol/{Uri.EscapeDataString(sourceDn)}/participants/{participantId}/stream");

        var response = await SendAuthorizedAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            EnsureCallControlResponseSucceeded(response.StatusCode, body);
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (new ResponseOwnedStream(stream, response), contentType);
    }

    public async Task SendParticipantAudioStreamAsync(
        string sourceDn,
        long participantId,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("ParticipantId must be a positive number.");
        }

        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/callcontrol/{Uri.EscapeDataString(sourceDn)}/participants/{participantId}/stream")
        {
            Content = content
        };

        using var response = await SendAuthorizedAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureCallControlResponseSucceeded(response.StatusCode, body);
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

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        ThrowIfDisposed();

        var bearerToken = await GetBearerTokenAsync(cancellationToken);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearerToken);

        var response = await (_apiClient?.SendAsync(request, completionOption, cancellationToken)
            ?? throw new InternalServerErrorException("3CX HTTP client is not initialized."));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _accessToken = null;
            var retryRequest = await CloneRequestAsync(request, cancellationToken);
            bearerToken = await GetBearerTokenAsync(cancellationToken);
            retryRequest.Headers.Authorization = AuthenticationHeaderValue.Parse(bearerToken);
            return await (_apiClient?.SendAsync(retryRequest, completionOption, cancellationToken)
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

    private sealed class ResponseOwnedStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseOwnedStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override ValueTask DisposeAsync()
        {
            _response.Dispose();
            return _inner.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _response.Dispose();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
