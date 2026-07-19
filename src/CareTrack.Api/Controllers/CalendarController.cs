using System.Net.Http.Json;
using System.Text.Json;
using CareTrack.Application;
using CareTrack.Infrastructure.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CareTrack.Api.Controllers;

/// <summary>
/// Connects a user's Google Calendar. Flow: the signed-in client calls
/// <c>connect</c> and sends the browser to the returned URL; Google redirects
/// back to <c>callback</c> (unauthenticated — correlated via the one-time
/// state token), which stores the refresh token and bounces to the web app.
/// </summary>
[ApiController]
[Route("api/calendar/google")]
public sealed class CalendarController : ControllerBase
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.events";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly IGoogleCalendarConnectionStore _connections;
    private readonly GoogleCalendarOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ICurrentUser _user;

    public CalendarController(
        IGoogleCalendarConnectionStore connections,
        IOptions<GoogleCalendarOptions> options,
        IMemoryCache cache,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ICurrentUser user)
    {
        _connections = connections;
        _options = options.Value;
        _cache = cache;
        _httpFactory = httpFactory;
        _config = config;
        _user = user;
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return Ok(new { available = false, connected = false });
        var connection = await _connections.GetAsync(_user.RequireUserId(), ct);
        return Ok(new { available = true, connected = connection is not null });
    }

    /// <summary>Returns the Google consent URL for the browser to visit.</summary>
    [Authorize]
    [HttpGet("connect")]
    public IActionResult Connect()
    {
        if (!_options.IsConfigured)
            return NotFound(new { error = "Google Calendar is not configured on this server." });

        var state = Guid.NewGuid().ToString("N");
        _cache.Set(StateKey(state), _user.RequireUserId(), StateLifetime);

        var url = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(_options.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scope)}"
            + "&access_type=offline"
            + "&prompt=consent"
            + $"&state={state}";
        return Ok(new { url });
    }

    /// <summary>Google's redirect target. Anonymous; trusts only the state token.</summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken ct)
    {
        if (!_options.IsConfigured) return NotFound();

        if (state is null || !_cache.TryGetValue(StateKey(state), out Guid userId))
            return BadRequest(new { error = "Unknown or expired state; start over." });
        _cache.Remove(StateKey(state)); // one-time use

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return Redirect(ClientRedirect("error"));

        var http = _httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri,
                ["grant_type"] = "authorization_code"
            }), ct);

        if (!resp.IsSuccessStatusCode)
            return Redirect(ClientRedirect("error"));

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var refreshToken = doc.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString() : null;
        if (string.IsNullOrEmpty(refreshToken))
            return Redirect(ClientRedirect("error"));

        await _connections.UpsertAsync(userId, refreshToken, ct);
        return Redirect(ClientRedirect("connected"));
    }

    [Authorize]
    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var userId = _user.RequireUserId();
        var connection = await _connections.GetAsync(userId, ct);
        if (connection is not null)
        {
            // Best-effort revoke at Google; local removal happens regardless.
            var http = _httpFactory.CreateClient();
            try
            {
                await http.PostAsync(
                    "https://oauth2.googleapis.com/revoke",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["token"] = connection.RefreshToken }), ct);
            }
            catch (HttpRequestException) { }
            await _connections.RemoveAsync(userId, ct);
        }
        return NoContent();
    }

    private static string StateKey(string state) => $"gcal-state:{state}";

    private string ClientRedirect(string result)
    {
        var origin = _config.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?.FirstOrDefault() ?? "/";
        return $"{origin}/?googleCalendar={result}";
    }
}
