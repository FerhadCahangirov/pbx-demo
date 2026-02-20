using System.Security.Claims;
using CallControl.Api.Domain;

namespace CallControl.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static string RequireSessionId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypesEx.SessionId);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UnauthorizedException("Session claim is missing.");
        }

        return value;
    }

    public static string RequireUsername(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UnauthorizedException("User claim is missing.");
        }

        return value;
    }

    public static int RequireUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypesEx.UserId);
        if (!int.TryParse(value, out var userId) || userId <= 0)
        {
            throw new UnauthorizedException("User id claim is missing.");
        }

        return userId;
    }
}
