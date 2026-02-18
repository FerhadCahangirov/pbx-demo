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

    public LoginResponse CreateToken(string sessionId, string username)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(1, _options.TokenLifetimeMinutes));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new(ClaimTypesEx.SessionId, sessionId)
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
            SessionId = sessionId,
            Username = username,
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAt
        };
    }
}
