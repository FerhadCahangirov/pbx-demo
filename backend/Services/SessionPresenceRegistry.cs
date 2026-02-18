using System.Collections.Concurrent;

namespace CallControl.Api.Services;

public sealed class SessionPresenceRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionsBySessionId = new(StringComparer.Ordinal);

    public void RegisterConnection(string sessionId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        var sessionConnections = _connectionsBySessionId.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        sessionConnections[connectionId] = 0;
    }

    public void UnregisterConnection(string sessionId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        if (!_connectionsBySessionId.TryGetValue(sessionId, out var sessionConnections))
        {
            return;
        }

        sessionConnections.TryRemove(connectionId, out _);
        if (sessionConnections.IsEmpty)
        {
            _connectionsBySessionId.TryRemove(sessionId, out _);
        }
    }

    public bool IsSessionOnline(string sessionId)
    {
        return _connectionsBySessionId.TryGetValue(sessionId, out var sessionConnections) && !sessionConnections.IsEmpty;
    }
}
