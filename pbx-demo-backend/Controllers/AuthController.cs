using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallControl.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(CancellationToken cancellationToken)
    {
        var request = new LoginRequest
        {
            Username = await RequestInputResolver.ResolveFieldAsync(Request, "username", cancellationToken) ?? string.Empty,
            Password = await RequestInputResolver.ResolveFieldAsync(Request, "password", cancellationToken) ?? string.Empty
        };

        var response = await _authService.LoginAsync(request, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.RequireSessionId();
        await _authService.LogoutAsync(sessionId);
        return NoContent();
    }
}
