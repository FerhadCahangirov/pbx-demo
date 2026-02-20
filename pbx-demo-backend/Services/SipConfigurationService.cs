using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class SipConfigurationService
{
    private readonly SoftphoneOptions _options;
    private readonly UserDirectoryService _userDirectoryService;

    public SipConfigurationService(
        IOptions<SoftphoneOptions> options,
        UserDirectoryService userDirectoryService)
    {
        _options = options.Value;
        _userDirectoryService = userDirectoryService;
    }

    public async Task<SipRegistrationConfigResponse> GetForUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var sipOptions = _options.SipWebRtc;
        if (!sipOptions.Enabled)
        {
            return new SipRegistrationConfigResponse
            {
                Enabled = false
            };
        }

        var webSocketUrl = sipOptions.WebSocketUrl?.Trim() ?? string.Empty;
        var domain = sipOptions.Domain?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(webSocketUrl) || string.IsNullOrWhiteSpace(domain))
        {
            throw new InternalServerErrorException("SIP WebRTC settings are not configured.");
        }

        var user = await _userDirectoryService.FindByUsernameAsync(username, cancellationToken)
            ?? throw new UnauthorizedException("User SIP profile is not configured.");

        var uriUser = FirstNonEmpty(user.SipUsername, user.OwnedExtension);
        var authId = FirstNonEmpty(user.SipAuthId, uriUser);
        var password = user.SipPassword?.Trim() ?? string.Empty;
        var displayName = FirstNonEmpty(user.SipDisplayName, $"{user.Username} ({user.OwnedExtension})");

        if (string.IsNullOrWhiteSpace(uriUser)
            || string.IsNullOrWhiteSpace(authId)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InternalServerErrorException("SIP credentials are missing for the current user.");
        }

        var iceServers = sipOptions.IceServers
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => server.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SipRegistrationConfigResponse
        {
            Enabled = true,
            WebSocketUrl = webSocketUrl,
            Domain = domain,
            Aor = $"sip:{uriUser}@{domain}",
            AuthorizationUsername = authId,
            AuthorizationPassword = password,
            DisplayName = displayName,
            IceServers = iceServers
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
