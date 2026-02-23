using System.Runtime.CompilerServices;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public interface IXapiQueueRealtimeAdapter
{
    string ExternalSchemaStatus { get; }
    IAsyncEnumerable<QueueInboundEventEnvelope> ReadEventsAsync(Guid tenantId, CancellationToken ct);
}

public sealed class XapiQueueRealtimeAdapterPlaceholder : IXapiQueueRealtimeAdapter
{
    public const string UndefinedSchemaStatus = "UNDEFINED EVENT SCHEMA IN OPENAPI";

    private readonly ILogger<XapiQueueRealtimeAdapterPlaceholder> _logger;

    public XapiQueueRealtimeAdapterPlaceholder(ILogger<XapiQueueRealtimeAdapterPlaceholder> logger)
    {
        _logger = logger;
    }

    public string ExternalSchemaStatus => UndefinedSchemaStatus;

    public async IAsyncEnumerable<QueueInboundEventEnvelope> ReadEventsAsync(
        Guid tenantId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug(
            "Queue realtime adapter placeholder invoked for tenant {TenantId}. {SchemaStatus}.",
            tenantId,
            UndefinedSchemaStatus);

        await Task.CompletedTask;
        yield break;
    }
}

