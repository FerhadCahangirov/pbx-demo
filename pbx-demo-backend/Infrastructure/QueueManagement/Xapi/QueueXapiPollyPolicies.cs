using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

internal static class QueueXapiPollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(
        QueueXapiClientOptions options,
        ILogger? logger,
        string clientLabel)
    {
        var retryCount = Math.Max(0, options.RetryCount);
        if (retryCount == 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }

        var baseDelayMs = Math.Max(50, options.RetryBaseDelayMs);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == (HttpStatusCode)429)
            .WaitAndRetryAsync(
                retryCount,
                retryAttempt => TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, retryAttempt - 1)),
                (outcome, delay, retryAttempt, _) =>
                {
                    logger?.LogWarning(
                        "Queue XAPI {ClientLabel} retry {RetryAttempt}/{RetryCount} after {DelayMs}ms. Status={StatusCode}.",
                        clientLabel,
                        retryAttempt,
                        retryCount,
                        delay.TotalMilliseconds,
                        outcome.Result?.StatusCode);
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(
        QueueXapiClientOptions options,
        ILogger? logger,
        string clientLabel)
    {
        var failureCount = Math.Max(2, options.CircuitBreakerFailureCount);
        var duration = TimeSpan.FromSeconds(Math.Max(5, options.CircuitBreakerDurationSeconds));

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == (HttpStatusCode)429)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: failureCount,
                durationOfBreak: duration,
                onBreak: (outcome, breakDelay) =>
                {
                    logger?.LogError(
                        "Queue XAPI {ClientLabel} circuit opened for {DelayMs}ms. Status={StatusCode}.",
                        clientLabel,
                        breakDelay.TotalMilliseconds,
                        outcome.Result?.StatusCode);
                },
                onReset: () => logger?.LogInformation("Queue XAPI {ClientLabel} circuit reset.", clientLabel),
                onHalfOpen: () => logger?.LogInformation("Queue XAPI {ClientLabel} circuit half-open.", clientLabel));
    }
}
