using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class ThreeCxClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SoftphoneOptions _options;

    public ThreeCxClientFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOptions<SoftphoneOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _options = options.Value;
    }

    public ThreeCxCallControlClient Create(ThreeCxConnectSettings settings)
    {
        return new ThreeCxCallControlClient(
            settings,
            _options.MaxWsReconnectAttempts,
            TimeSpan.FromSeconds(Math.Max(1, _options.WsReconnectDelaySeconds)),
            _httpClientFactory,
            _loggerFactory.CreateLogger<ThreeCxCallControlClient>());
    }
}
