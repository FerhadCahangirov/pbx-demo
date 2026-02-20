using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CallControl.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallControl.Api.Services;

public sealed class JwtTokenService
{
    private readonly SoftphoneOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<SoftphoneOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.JwtSigningKey) || _options.JwtSigningKey.Length < 32)
        {
            throw new InvalidOperationException("Softphone:JwtSigningKey must be at least 32 characters.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public LoginResponse CreateToken(
        string sessionId,
        AppUserRecord user,
        string pbxBase,
        bool hasSoftphoneAccess)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(1, _options.TokenLifetimeMinutes));
        var role = user.Role.ToString();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, role),
            new(ClaimTypesEx.SessionId, sessionId),
            new(ClaimTypesEx.UserId, user.Id.ToString())
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.JwtIssuer,
            Audience = _options.JwtAudience,
            Expires = expiresAt.UtcDateTime,
            NotBefore = now.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        var accessToken = handler.WriteToken(token);

        return new LoginResponse
        {
            UserId = user.Id,
            SessionId = sessionId,
            Username = user.Username,
            DisplayName = BuildDisplayName(user),
            Role = role,
            HasSoftphoneAccess = hasSoftphoneAccess,
            OwnedExtensionDn = user.OwnedExtension,
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAt,
            PbxBase = pbxBase
        };
    }

    private static string BuildDisplayName(AppUserRecord user)
    {
        var firstName = user.FirstName?.Trim() ?? string.Empty;
        var lastName = user.LastName?.Trim() ?? string.Empty;
        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }
}
