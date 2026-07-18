using CareTrack.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest req, CancellationToken ct)
    {
        try { return Ok(await _auth.RegisterAsync(req, ct)); }
        catch (AuthException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req, CancellationToken ct)
    {
        try { return Ok(await _auth.LoginAsync(req, ct)); }
        catch (AuthException) { return Unauthorized(new { error = "Invalid credentials." }); }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest req, CancellationToken ct)
    {
        try { return Ok(await _auth.RefreshAsync(req, ct)); }
        catch (AuthException) { return Unauthorized(new { error = "Invalid refresh token." }); }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest req, CancellationToken ct)
    {
        await _auth.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }
}
