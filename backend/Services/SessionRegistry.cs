using System.Collections.Concurrent;
using CallControl.Api.Domain;

namespace CallControl.Api.Services;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, SoftphoneSession> _sessions = new(StringComparer.Ordinal);

    public bool TryGet(string sessionId, out SoftphoneSession session)
    {
        return _sessions.TryGetValue(sessionId, out session!);
    }

    public void Add(SoftphoneSession session)
    {
        if (!_sessions.TryAdd(session.SessionId, session))
        {
            throw new InvalidOperationException($"Session '{session.SessionId}' already exists.");
        }
    }

    public async Task RemoveAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    public IReadOnlyList<SoftphoneSession> List()
    {
        return _sessions.Values.ToList();
    }
}
