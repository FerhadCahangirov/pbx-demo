using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueOutboxSignalrPublisherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueOutboxSignalrPublisherWorker> _logger;

    public QueueOutboxSignalrPublisherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<QueueOutboxSignalrPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupDelay = TimeSpan.FromSeconds(3);
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<IQueueOutboxSignalrPublisher>();
                processed = await publisher.ProcessPendingAsync(stoppingToken);

                if (processed > 0)
                {
                    _logger.LogDebug("Queue outbox SignalR publisher processed {Processed} messages.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue outbox SignalR publisher worker failed.");
            }

            var delay = processed > 0
                ? TimeSpan.FromMilliseconds(250)
                : TimeSpan.FromMilliseconds(750);

            await Task.Delay(delay, stoppingToken);
        }
    }
}
