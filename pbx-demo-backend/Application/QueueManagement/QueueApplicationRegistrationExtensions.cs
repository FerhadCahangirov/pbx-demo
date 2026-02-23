using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public static class QueueApplicationRegistrationExtensions
{
    // Batch 6 registration extension only.
    // Program.cs wiring remains deferred.
    public static IServiceCollection AddQueueManagementApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<QueueApplicationOptions>()
            .Bind(configuration.GetSection(QueueApplicationOptions.SectionName));
        services.AddOptions<QueueAnalyticsPreAggregationWorker.QueueAnalyticsOptions>()
            .Bind(configuration.GetSection(QueueAnalyticsPreAggregationWorker.QueueAnalyticsOptions.SectionName));

        return services.AddQueueManagementApplicationCore();
    }

    public static IServiceCollection AddQueueManagementApplication(
        this IServiceCollection services,
        Action<QueueApplicationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<QueueApplicationOptions>()
            .Configure(configure);
        services.AddOptions<QueueAnalyticsPreAggregationWorker.QueueAnalyticsOptions>();

        return services.AddQueueManagementApplicationCore();
    }

    public static IServiceCollection AddQueueManagementApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<QueueApplicationOptions>();
        services.AddOptions<QueueAnalyticsPreAggregationWorker.QueueAnalyticsOptions>();
        return services.AddQueueManagementApplicationCore();
    }

    private static IServiceCollection AddQueueManagementApplicationCore(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.TryAddSingleton<IQueueHubMessagePublisherTransport, QueueHubMessagePublisherTransportPlaceholder>();

        services.TryAddScoped<QueueApplicationMapper>();
        services.TryAddScoped<QueueLiveSnapshotBuilder>();
        services.TryAddScoped<QueueKpiCalculator>();
        services.TryAddScoped<QueueTimeSeriesAnalyzer>();
        services.TryAddScoped<QueueComparisonEngine>();
        services.TryAddScoped<QueueAgentRankingEngine>();
        services.TryAddScoped<QueueAnalyticsPreAggregationWorker.QueueAnalyticsBucketAggregator>();
        services.TryAddScoped<IQueueOutboxSignalrPublisher, QueueOutboxSignalrPublisher>();
        services.TryAddScoped<QueueOutboxSignalrPublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QueueAnalyticsPreAggregationWorker>());

        services.TryAddScoped<IQueueService, QueueService>();
        services.TryAddScoped<IQueueLiveStateService, QueueLiveStateService>();
        services.TryAddScoped<IQueueAnalyticsService, QueueAnalyticsService>();

        return services;
    }
}

public sealed class QueueApplicationOptions
{
    public const string SectionName = "QueueManagement:Batch6Application";

    public int LiveSnapshotCacheSeconds { get; set; } = 2;
    public int AnalyticsCacheSeconds { get; set; } = 15;
    public int OutboxPublishBatchSize { get; set; } = 100;
}
