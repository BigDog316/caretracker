using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CareTrack.Client.Shared.Api;

/// <summary>
/// Typed client for the CareTrack API shared by the web and mobile hosts.
/// Holds the signed-in state, attaches the bearer token, and transparently
/// refreshes + retries once when an access token has expired.
/// </summary>
public sealed class CareTrackApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private StoredTokens? _tokens;
    private bool _initialized;

    public CareTrackApi(HttpClient http, ITokenStore store)
    {
        _http = http;
        _store = store;
    }

    /// <summary>Fires when the user signs in or out.</summary>
    public event Action? AuthChanged;

    public bool IsAuthenticated => _tokens is not null;
    public Guid? UserId => _tokens?.UserId;

    /// <summary>Loads persisted tokens once per app start.</summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _tokens = await _store.LoadAsync();
        // The layout renders before any page initializes us; let it know a
        // persisted session was restored so signed-in chrome appears.
        if (_tokens is not null) AuthChanged?.Invoke();
    }

    // ---- Auth ----

    public async Task LoginAsync(string email, string password)
    {
        var resp = await _http.PostAsJsonAsync(
            "api/auth/login", new { email, password }, Json);
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("That email and password didn't match.");
        await StoreAuthAsync(resp);
    }

    public async Task RegisterAsync(string email, string password, string displayName)
    {
        var resp = await _http.PostAsJsonAsync(
            "api/auth/register", new { email, password, displayName }, Json);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await ReadErrorAsync(resp);
            throw new ApiException(detail ?? "Registration failed.");
        }
        await StoreAuthAsync(resp);
    }

    public async Task LogoutAsync()
    {
        if (_tokens is not null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
                {
                    Content = JsonContent.Create(
                        new { refreshToken = _tokens.RefreshToken }, options: Json)
                };
                req.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
                await _http.SendAsync(req);
            }
            catch (HttpRequestException)
            {
                // Offline logout still clears local state.
            }
        }
        _tokens = null;
        await _store.ClearAsync();
        AuthChanged?.Invoke();
    }

    // ---- Care profiles ----

    public Task<IReadOnlyList<CareProfileSummary>> GetProfilesAsync()
        => GetAsync<IReadOnlyList<CareProfileSummary>>("api/care-profiles");

    public async Task<CareProfileSummary> CreateProfileAsync(
        string displayName, DateOnly? dateOfBirth)
    {
        var resp = await SendAuthedAsync(() =>
            new HttpRequestMessage(HttpMethod.Post, "api/care-profiles")
            {
                Content = JsonContent.Create(new { displayName, dateOfBirth }, options: Json)
            });
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await ReadErrorAsync(resp);
            throw new ApiException(detail ?? "Couldn't create the profile.");
        }
        return (await resp.Content.ReadFromJsonAsync<CareProfileSummary>(Json))!;
    }

    // ---- Profile detail: providers / appointments / notes ----

    public Task<IReadOnlyList<ProviderSummary>> GetProvidersAsync(Guid profileId)
        => GetAsync<IReadOnlyList<ProviderSummary>>(
            $"api/care-profiles/{profileId}/providers");

    public async Task AddProviderAsync(
        Guid profileId, string name, string? organization, string? specialty,
        string? address, string? phone)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"api/care-profiles/{profileId}/providers")
        {
            Content = JsonContent.Create(
                new { name, organization, specialty, address, phone }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't add the provider.");
    }

    public Task<IReadOnlyList<AppointmentSummary>> GetAppointmentsAsync(Guid profileId)
        => GetAsync<IReadOnlyList<AppointmentSummary>>(
            $"api/care-profiles/{profileId}/appointments");

    public async Task AddAppointmentAsync(
        Guid profileId, string title, DateTimeOffset startsAt, DateTimeOffset endsAt,
        Guid? providerId, string? location)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"api/care-profiles/{profileId}/appointments")
        {
            Content = JsonContent.Create(
                new { title, startsAt, endsAt, providerId, location }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't add the appointment.");
    }

    public Task<IReadOnlyList<NoteSummary>> GetNotesAsync(
        Guid profileId, string? keyword = null)
        => GetAsync<IReadOnlyList<NoteSummary>>(
            $"api/care-profiles/{profileId}/notes"
            + (string.IsNullOrWhiteSpace(keyword)
                ? "" : $"?q={Uri.EscapeDataString(keyword)}"));

    public async Task AddNoteAsync(
        Guid profileId, string body, Guid? appointmentId, Guid? providerId)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"api/care-profiles/{profileId}/notes")
        {
            Content = JsonContent.Create(
                new { body, appointmentId, providerId }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't save the note.");
    }

    // ---- Follow-ups ----

    public Task<IReadOnlyList<FollowUpReminder>> GetFollowUpsAsync()
        => GetAsync<IReadOnlyList<FollowUpReminder>>("api/reminders/follow-ups");

    public async Task CompleteFollowUpAsync(
        Guid careProfileId, Guid appointmentId, string? note)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post,
            $"api/care-profiles/{careProfileId}/appointments/{appointmentId}/follow-up")
        {
            Content = JsonContent.Create(new { note }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't save the follow-up.");
    }

    // ---- Plumbing ----

    private async Task<T> GetAsync<T>(string url)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Something went wrong loading data.");
        return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>
    /// Sends with the bearer token; on 401 refreshes once and retries. The
    /// request is rebuilt via the factory because HttpRequestMessage is
    /// single-use.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthedAsync(
        Func<HttpRequestMessage> requestFactory)
    {
        if (_tokens is null)
            throw new ApiException("You're signed out.");

        var resp = await SendWithBearerAsync(requestFactory);
        if (resp.StatusCode != HttpStatusCode.Unauthorized)
            return resp;

        if (!await TryRefreshAsync())
            throw new ApiException("Your session expired. Please sign in again.");
        return await SendWithBearerAsync(requestFactory);
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(
        Func<HttpRequestMessage> requestFactory)
    {
        using var req = requestFactory();
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
        return await _http.SendAsync(req);
    }

    private async Task<bool> TryRefreshAsync()
    {
        if (_tokens is null) return false;
        var resp = await _http.PostAsJsonAsync(
            "api/auth/refresh", new { refreshToken = _tokens.RefreshToken }, Json);
        if (!resp.IsSuccessStatusCode)
        {
            _tokens = null;
            await _store.ClearAsync();
            AuthChanged?.Invoke();
            return false;
        }
        await StoreAuthAsync(resp, notify: false);
        return true;
    }

    private async Task StoreAuthAsync(HttpResponseMessage resp, bool notify = true)
    {
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResult>(Json))!;
        _tokens = new StoredTokens(auth.UserId, auth.AccessToken, auth.RefreshToken);
        await _store.SaveAsync(_tokens);
        if (notify) AuthChanged?.Invoke();
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
            return doc.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
