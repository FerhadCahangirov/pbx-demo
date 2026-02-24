using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public static class QueueIngestionRegistrationExtensions
{
    // Batch 5 registration extension only.
    // Program.cs wiring is intentionally deferred to a later batch.
    public static IServiceCollection AddQueueManagementBatch5Ingestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<QueueIngestionOptions>()
            .Bind(configuration.GetSection(QueueIngestionOptions.SectionName));

        return services.AddQueueManagementBatch5IngestionCore();
    }

    public static IServiceCollection AddQueueManagementBatch5Ingestion(
        this IServiceCollection services,
        Action<QueueIngestionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<QueueIngestionOptions>()
            .Configure(configure);

        return services.AddQueueManagementBatch5IngestionCore();
    }

    public static IServiceCollection AddQueueManagementBatch5Ingestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<QueueIngestionOptions>();
        return services.AddQueueManagementBatch5IngestionCore();
    }

    private static IServiceCollection AddQueueManagementBatch5IngestionCore(this IServiceCollection services)
    {
        services.TryAddSingleton<QueueEventIdempotencyKeyFactory>();
        services.TryAddSingleton<IXapiQueueRealtimeAdapter, XapiQueueRealtimeAdapterPlaceholder>();
        services.TryAddSingleton<QueueThreeCxWebSocketIngestionBridge>();

        services.TryAddScoped<QueueReconciliationMapper>();
        services.TryAddScoped<QueueCallLifecycleManager>();
        services.TryAddScoped<QueueEventOrderingDispatcher>();
        services.TryAddScoped<IQueueEventProcessor, QueueEventProcessor>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ActiveCallsPollingWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CallHistoryReconciliationWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CallLogReconciliationWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QueueEventInboxProcessorWorker>());

        return services;
    }
}

public sealed class QueueIngestionOptions
{
    public const string SectionName = "QueueManagement:Batch5Ingestion";

    public bool EnableActiveCallsPolling { get; set; } = true;
    public bool EnableCallHistoryReconciliation { get; set; } = true;
    public bool EnableCallLogReconciliation { get; set; } = true;
    public bool EnableInboxProcessor { get; set; } = true;
    public bool EnableRealtimeAdapterPlaceholder { get; set; }
    public int WorkerStartupDelaySeconds { get; set; } = 3;

    public int ActiveCallsPollingIntervalSeconds { get; set; } = 5;
    public int ActiveCallsTop { get; set; } = 100;
    public int ActiveCallsMissingCompletionLookbackMinutes { get; set; } = 240;

    public int CallHistoryReconciliationIntervalSeconds { get; set; } = 60;
    public int CallLogReconciliationIntervalSeconds { get; set; } = 120;
    public int ReconciliationLookbackMinutes { get; set; } = 30;
    public int ReconciliationPageSize { get; set; } = 100;
    public List<string> CallLogFunctionPaths { get; set; } = [];

    public int InboxPollingIntervalSeconds { get; set; } = 2;
    public int InboxBatchSize { get; set; } = 200;
    public int InboxMaxAttempts { get; set; } = 8;
    public int InboxRetryBaseDelaySeconds { get; set; } = 5;
    public int InboxProcessingLeaseTimeoutSeconds { get; set; } = 120;
}

internal static class QueueBatch5IngestionOptionsExtensions
{
    public static TimeSpan GetWorkerStartupDelay(this QueueIngestionOptions options)
        => TimeSpan.FromSeconds(Math.Max(0, options.WorkerStartupDelaySeconds));

    public static TimeSpan GetActiveCallsPollingInterval(this QueueIngestionOptions options)
        => TimeSpan.FromSeconds(Math.Max(1, options.ActiveCallsPollingIntervalSeconds));

    public static TimeSpan GetCallHistoryReconciliationInterval(this QueueIngestionOptions options)
        => TimeSpan.FromSeconds(Math.Max(5, options.CallHistoryReconciliationIntervalSeconds));

    public static TimeSpan GetCallLogReconciliationInterval(this QueueIngestionOptions options)
        => TimeSpan.FromSeconds(Math.Max(5, options.CallLogReconciliationIntervalSeconds));

    public static TimeSpan GetInboxPollingInterval(this QueueIngestionOptions options)
        => TimeSpan.FromSeconds(Math.Max(1, options.InboxPollingIntervalSeconds));

    public static TimeSpan GetReconciliationLookback(this QueueIngestionOptions options)
        => TimeSpan.FromMinutes(Math.Max(1, options.ReconciliationLookbackMinutes));
}
