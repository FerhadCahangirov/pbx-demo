using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class AuthService
{
    private readonly SoftphoneOptions _options;
    private readonly JwtTokenService _jwtTokenService;
    private readonly CallManager _callManager;
    private readonly WebRtcCallManager _webRtcCallManager;

    public AuthService(
        IOptions<SoftphoneOptions> options,
        JwtTokenService jwtTokenService,
        CallManager callManager,
        WebRtcCallManager webRtcCallManager)
    {
        _options = options.Value;
        _jwtTokenService = jwtTokenService;
        _callManager = callManager;
        _webRtcCallManager = webRtcCallManager;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BadRequestException("Username and password are required.");
        }

        if (string.IsNullOrWhiteSpace(request.PbxBase)
            || string.IsNullOrWhiteSpace(request.AppId)
            || string.IsNullOrWhiteSpace(request.AppSecret))
        {
            throw new BadRequestException("PBX base URL, app id, and app secret are required.");
        }

        var user = _options.Users.FirstOrDefault(u => string.Equals(u.Username, request.Username.Trim(), StringComparison.Ordinal));
        if (user is null || !string.Equals(user.Password, request.Password, StringComparison.Ordinal))
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        if (string.IsNullOrWhiteSpace(user.OwnedExtension))
        {
            throw new ForbiddenException("The user does not own any extension.");
        }

        var sessionId = Guid.NewGuid().ToString("N");

        await _callManager.CreateSessionAsync(
            sessionId,
            user.Username,
            user.OwnedExtension,
            new ThreeCxConnectSettings
            {
                PbxBase = request.PbxBase,
                AppId = request.AppId,
                AppSecret = request.AppSecret
            },
            cancellationToken);

        return _jwtTokenService.CreateToken(sessionId, user.Username);
    }

    public async Task LogoutAsync(string sessionId)
    {
        await _webRtcCallManager.HandleSessionDisconnectedAsync(sessionId);
        await _callManager.DisconnectSessionAsync(sessionId);
    }
}
