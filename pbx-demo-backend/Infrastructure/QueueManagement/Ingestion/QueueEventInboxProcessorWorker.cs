using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueEventInboxProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<QueueIngestionOptions> _optionsMonitor;
    private readonly ILogger<QueueEventInboxProcessorWorker> _logger;

    public QueueEventInboxProcessorWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<QueueIngestionOptions> optionsMonitor,
        ILogger<QueueEventInboxProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupDelay = _optionsMonitor.CurrentValue.GetWorkerStartupDelay();
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var delay = options.GetInboxPollingInterval();

            if (!options.EnableInboxProcessor)
            {
                await Task.Delay(delay, stoppingToken);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<QueueEventOrderingDispatcher>();

                while (!stoppingToken.IsCancellationRequested)
                {
                    var count = await dispatcher.DispatchNextBatchAsync(stoppingToken);
                    if (count <= 0 || count < Math.Max(1, options.InboxBatchSize))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue inbox processor worker failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
