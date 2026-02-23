using System.Net.Http.Headers;
using CallControl.Api.Domain;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Xapi;

public static class QueueXapiServiceCollectionExtensions
{
    // Batch 4 provides infrastructure registration primitives only.
    // Program.cs wiring is deferred to the API/presentation batch.
    public static IServiceCollection AddQueueXapiInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<QueueXapiClientOptions>()
            .Configure<IOptions<SoftphoneOptions>>((target, softphoneOptionsAccessor) =>
            {
                var source = softphoneOptionsAccessor.Value.ThreeCx;
                target.PbxBase = source.PbxBase;
                target.ClientId = source.AppId;
                target.ClientSecret = source.AppSecret;
            });

        services.AddHttpClient(QueueXapiHttpClientNames.Api, ConfigureClient)
            .AddPolicyHandler((IServiceProvider sp, HttpRequestMessage _) => QueueXapiPollyPolicies.CreateRetryPolicy(
                sp.GetRequiredService<IOptions<QueueXapiClientOptions>>().Value,
                sp.GetService<ILoggerFactory>()?.CreateLogger("QueueXapi.ApiHttpClient"),
                "api"))
            .AddPolicyHandler((IServiceProvider sp, HttpRequestMessage _) => QueueXapiPollyPolicies.CreateCircuitBreakerPolicy(
                sp.GetRequiredService<IOptions<QueueXapiClientOptions>>().Value,
                sp.GetService<ILoggerFactory>()?.CreateLogger("QueueXapi.ApiHttpClient"),
                "api"));

        services.AddHttpClient(QueueXapiHttpClientNames.Token, ConfigureClient)
            .AddPolicyHandler((IServiceProvider sp, HttpRequestMessage _) => QueueXapiPollyPolicies.CreateRetryPolicy(
                sp.GetRequiredService<IOptions<QueueXapiClientOptions>>().Value,
                sp.GetService<ILoggerFactory>()?.CreateLogger("QueueXapi.TokenHttpClient"),
                "token"))
            .AddPolicyHandler((IServiceProvider sp, HttpRequestMessage _) => QueueXapiPollyPolicies.CreateCircuitBreakerPolicy(
                sp.GetRequiredService<IOptions<QueueXapiClientOptions>>().Value,
                sp.GetService<ILoggerFactory>()?.CreateLogger("QueueXapi.TokenHttpClient"),
                "token"));

        services.TryAddSingleton<IQueueXapiAccessTokenProvider, QueueXapiAccessTokenProvider>();
        services.TryAddSingleton<IQueueXapiClient, QueueXapiClient>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<QueueXapiClientOptions>>().Value;
        client.BaseAddress = options.GetAuthorityBaseUri();
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
