using Microsoft.Extensions.Options;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class CallLogReconciliationWorker : BackgroundService
{

    private readonly IOptionsMonitor<QueueIngestionOptions> _optionsMonitor;
    private readonly ILogger<CallLogReconciliationWorker> _logger;

    public CallLogReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<QueueIngestionOptions> optionsMonitor,
        ILogger<CallLogReconciliationWorker> logger)
    {
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
            var interval = options.GetCallLogReconciliationInterval();

            if (!options.EnableCallLogReconciliation)
            {
                await Task.Delay(interval, stoppingToken);
                continue;
            }

            var configuredPaths = options.CallLogFunctionPaths
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();

            if (configuredPaths.Length == 0)
            {
                _logger.LogDebug(
                    "CallLog reconciliation skipped: no configured /ReportCallLogData function paths. Documented function path parameters must be provided explicitly.");
                await Task.Delay(interval, stoppingToken);
                continue;
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

}
