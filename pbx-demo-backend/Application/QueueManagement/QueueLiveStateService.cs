using CallControl.Api.Hubs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueLiveStateService : IQueueLiveStateService
{
    private readonly QueueLiveSnapshotBuilder _builder;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<QueueApplicationOptions> _optionsMonitor;
    private readonly IQueueHubMessagePublisherTransport _transport;
    private readonly ILogger<QueueLiveStateService> _logger;

    public QueueLiveStateService(
        QueueLiveSnapshotBuilder builder,
        IMemoryCache cache,
        IOptionsMonitor<QueueApplicationOptions> optionsMonitor,
        IQueueHubMessagePublisherTransport transport,
        ILogger<QueueLiveStateService> logger)
    {
        _builder = builder;
        _cache = cache;
        _optionsMonitor = optionsMonitor;
        _transport = transport;
        _logger = logger;
    }

    public async Task<QueueLiveSnapshotDto> GetSnapshotAsync(long queueId, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(queueId);

        if (_cache.TryGetValue(cacheKey, out QueueLiveSnapshotDto? cached) && cached is not null)
        {
            return cached;
        }

        var snapshot = await _builder.BuildAsync(queueId, ct);
        var ttl = TimeSpan.FromSeconds(Math.Max(0, _optionsMonitor.CurrentValue.LiveSnapshotCacheSeconds));
        if (ttl > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, snapshot, ttl);
        }

        return snapshot;
    }

    public async Task PublishSnapshotAsync(long queueId, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(queueId);
        _cache.Remove(cacheKey);

        var snapshot = await _builder.BuildAsync(queueId, ct);
        var ttl = TimeSpan.FromSeconds(Math.Max(0, _optionsMonitor.CurrentValue.LiveSnapshotCacheSeconds));
        if (ttl > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, snapshot, ttl);
        }

        await _transport.PublishWaitingListUpdatedAsync(new QueueWaitingListUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Version = snapshot.Version,
            WaitingCalls = snapshot.WaitingCalls
        }, ct);

        await _transport.PublishActiveCallsUpdatedAsync(new QueueActiveCallsUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Version = snapshot.Version,
            ActiveCalls = snapshot.ActiveCalls
        }, ct);

        await _transport.PublishStatsUpdatedAsync(new QueueStatsUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Stats = snapshot.Stats
        }, ct);

        _logger.LogDebug(
            "Published queue snapshot for queue {QueueId}. Waiting={WaitingCount}, Active={ActiveCount}, Version={Version}.",
            snapshot.QueueId,
            snapshot.WaitingCalls.Count,
            snapshot.ActiveCalls.Count,
            snapshot.Version);
    }

    private static string GetCacheKey(long queueId)
        => $"queue-live-snapshot:{queueId}";
}
