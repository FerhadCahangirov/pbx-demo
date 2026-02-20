using CallControl.Api.Domain;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class AuthService
{
    private readonly SoftphoneOptions _options;
    private readonly UserDirectoryService _userDirectoryService;
    private readonly PasswordHasher _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;
    private readonly CallManager _callManager;
    private readonly WebRtcCallManager _webRtcCallManager;

    public AuthService(
        IOptions<SoftphoneOptions> options,
        UserDirectoryService userDirectoryService,
        PasswordHasher passwordHasher,
        JwtTokenService jwtTokenService,
        CallManager callManager,
        WebRtcCallManager webRtcCallManager)
    {
        _options = options.Value;
        _userDirectoryService = userDirectoryService;
        _passwordHasher = passwordHasher;
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

        var user = await _userDirectoryService.FindByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null
            || !user.IsActive
            || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        var pbxBase = _options.ThreeCx.PbxBase?.Trim() ?? string.Empty;
        var appId = _options.ThreeCx.AppId?.Trim() ?? string.Empty;
        var appSecret = _options.ThreeCx.AppSecret?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pbxBase)
            || string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new InternalServerErrorException("3CX app credentials are not configured on server.");
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var hasSoftphoneAccess = !string.IsNullOrWhiteSpace(user.OwnedExtension);

        if (hasSoftphoneAccess)
        {
            await _callManager.CreateSessionAsync(
                sessionId,
                user.Id,
                user.Username,
                user.OwnedExtension,
                user.ControlDn,
                new ThreeCxConnectSettings
                {
                    PbxBase = pbxBase,
                    AppId = appId,
                    AppSecret = appSecret
                },
                cancellationToken);
        }

        return _jwtTokenService.CreateToken(sessionId, user, pbxBase, hasSoftphoneAccess);
    }

    public async Task LogoutAsync(string sessionId)
    {
        await _webRtcCallManager.HandleSessionDisconnectedAsync(sessionId);
        await _callManager.DisconnectSessionAsync(sessionId);
    }
}
