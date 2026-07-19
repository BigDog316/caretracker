using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CareTrack.Infrastructure.Calendar;

public sealed class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>Must match an authorized redirect URI on the OAuth client.</summary>
    public string RedirectUri { get; set; } =
        "http://localhost:5210/api/calendar/google/callback";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Google Calendar sync over the REST API (no SDK dependency): exchanges the
/// stored per-user refresh token for an access token, then writes events to
/// the user's primary calendar. Best-effort throughout — any failure logs and
/// leaves the appointment unsynced rather than failing the request.
/// </summary>
public sealed class GoogleCalendarSync : ICalendarSync
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string EventsEndpoint =
        "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    private readonly HttpClient _http;
    private readonly IGoogleCalendarConnectionStore _connections;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleCalendarSync> _logger;

    public GoogleCalendarSync(
        HttpClient http,
        IGoogleCalendarConnectionStore connections,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleCalendarSync> logger)
    {
        _http = http;
        _connections = connections;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> CreateEventAsync(
        Guid userId, Appointment appointment, CancellationToken ct = default)
    {
        var access = await AccessTokenForAsync(userId, ct);
        if (access is null) return null;

        using var req = new HttpRequestMessage(HttpMethod.Post, EventsEndpoint)
        { Content = JsonContent.Create(EventBody(appointment)) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Google event create failed ({Status}) for appointment {AppointmentId}.",
                (int)resp.StatusCode, appointment.Id);
            return null;
        }

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return doc.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task UpdateEventAsync(
        Guid userId, Appointment appointment, CancellationToken ct = default)
    {
        if (appointment.ExternalCalendarEventId is not string eventId) return;
        var access = await AccessTokenForAsync(userId, ct);
        if (access is null) return;

        using var req = new HttpRequestMessage(
            HttpMethod.Put, $"{EventsEndpoint}/{Uri.EscapeDataString(eventId)}")
        { Content = JsonContent.Create(EventBody(appointment)) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning(
                "Google event update failed ({Status}) for appointment {AppointmentId}.",
                (int)resp.StatusCode, appointment.Id);
    }

    public async Task CancelEventAsync(
        Guid userId, string externalEventId, CancellationToken ct = default)
    {
        var access = await AccessTokenForAsync(userId, ct);
        if (access is null) return;

        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"{EventsEndpoint}/{Uri.EscapeDataString(externalEventId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning(
                "Google event cancel failed ({Status}) for event {EventId}.",
                (int)resp.StatusCode, externalEventId);
    }

    private static object EventBody(Appointment a) => new
    {
        summary = a.Title,
        location = a.Location,
        start = new { dateTime = a.StartsAt.UtcDateTime.ToString("o"), timeZone = "UTC" },
        end = new { dateTime = a.EndsAt.UtcDateTime.ToString("o"), timeZone = "UTC" }
    };

    /// <summary>Refresh-token grant; null when not connected or refresh fails.</summary>
    private async Task<string?> AccessTokenForAsync(Guid userId, CancellationToken ct)
    {
        var connection = await _connections.GetAsync(userId, ct);
        if (connection is null) return null;

        var resp = await _http.PostAsync(TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = connection.RefreshToken,
                ["grant_type"] = "refresh_token"
            }), ct);

        if (!resp.IsSuccessStatusCode)
        {
            // A revoked/expired grant (e.g. testing-mode 7-day expiry) means
            // the user must reconnect; drop the dead connection.
            _logger.LogWarning(
                "Google token refresh failed ({Status}) for user {UserId}; " +
                "removing stale connection.", (int)resp.StatusCode, userId);
            await _connections.RemoveAsync(userId, ct);
            return null;
        }

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return doc.TryGetProperty("access_token", out var token)
            ? token.GetString() : null;
    }
}
