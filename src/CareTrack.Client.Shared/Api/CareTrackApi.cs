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

    // ---- Documents ----

    public Task<IReadOnlyList<DocumentSummary>> GetDocumentsAsync(
        Guid profileId, string? tag = null)
        => GetAsync<IReadOnlyList<DocumentSummary>>(
            $"api/care-profiles/{profileId}/documents"
            + (string.IsNullOrWhiteSpace(tag)
                ? "" : $"?tag={Uri.EscapeDataString(tag)}"));

    public async Task UploadDocumentAsync(
        Guid profileId, string fileName, string contentType, Stream content,
        string? description, IReadOnlyCollection<string> tags)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var form = new MultipartFormDataContent();
            var filePart = new StreamContent(content);
            if (!string.IsNullOrWhiteSpace(contentType))
                filePart.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(filePart, "file", fileName);
            if (!string.IsNullOrWhiteSpace(description))
                form.Add(new StringContent(description), "description");
            foreach (var tag in tags)
                form.Add(new StringContent(tag), "tags");
            return new HttpRequestMessage(HttpMethod.Post,
                $"api/care-profiles/{profileId}/documents")
            { Content = form };
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't upload the file.");
    }

    public async Task<FileDownload> DownloadDocumentAsync(
        Guid profileId, Guid documentId)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Get,
            $"api/care-profiles/{profileId}/documents/{documentId}/content"));
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't download the file.");
        return new FileDownload(
            resp.Content.Headers.ContentDisposition?.FileNameStar
                ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? "document",
            resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            await resp.Content.ReadAsByteArrayAsync());
    }

    public async Task DeleteDocumentAsync(Guid profileId, Guid documentId)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Delete,
            $"api/care-profiles/{profileId}/documents/{documentId}"));
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't delete the file.");
    }

    // ---- Cards ----

    public Task<IReadOnlyList<string>> GetCardSectionsAsync(Guid profileId)
        => GetAsync<IReadOnlyList<string>>(
            $"api/care-profiles/{profileId}/cards/sections");

    public Task<IReadOnlyList<CardSummary>> GetCardsAsync(
        Guid profileId, string? section = null)
        => GetAsync<IReadOnlyList<CardSummary>>(
            $"api/care-profiles/{profileId}/cards"
            + (string.IsNullOrWhiteSpace(section)
                ? "" : $"?section={Uri.EscapeDataString(section)}"));

    public async Task UploadCardAsync(
        Guid profileId, string fileName, string contentType, Stream image,
        string section, string? label, string? description)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var form = new MultipartFormDataContent();
            var filePart = new StreamContent(image);
            if (!string.IsNullOrWhiteSpace(contentType))
                filePart.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(filePart, "image", fileName);
            form.Add(new StringContent(section), "section");
            if (!string.IsNullOrWhiteSpace(label))
                form.Add(new StringContent(label), "label");
            if (!string.IsNullOrWhiteSpace(description))
                form.Add(new StringContent(description), "description");
            return new HttpRequestMessage(HttpMethod.Post,
                $"api/care-profiles/{profileId}/cards")
            { Content = form };
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't add the card.");
    }

    public async Task<FileDownload> GetCardImageAsync(Guid profileId, Guid cardId)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Get, $"api/care-profiles/{profileId}/cards/{cardId}/image"));
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't load the card image.");
        return new FileDownload(
            "card-image",
            resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            await resp.Content.ReadAsByteArrayAsync());
    }

    public async Task DeleteCardAsync(Guid profileId, Guid cardId)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Delete, $"api/care-profiles/{profileId}/cards/{cardId}"));
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't delete the card.");
    }

    // ---- School plans + agencies ----

    public Task<IReadOnlyList<SchoolPlanSummary>> GetSchoolPlansAsync(Guid profileId)
        => GetAsync<IReadOnlyList<SchoolPlanSummary>>(
            $"api/care-profiles/{profileId}/school-plans");

    public Task<IReadOnlyList<SchoolPlanSummary>> GetUpcomingReviewsAsync(
        Guid profileId, int withinDays = 60)
        => GetAsync<IReadOnlyList<SchoolPlanSummary>>(
            $"api/care-profiles/{profileId}/school-plans/upcoming-reviews?withinDays={withinDays}");

    public async Task AddSchoolPlanAsync(
        Guid profileId, string type, string? school, string? title,
        DateOnly? reviewDueOn)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"api/care-profiles/{profileId}/school-plans")
        {
            Content = JsonContent.Create(
                new { type, school, title, reviewDueOn }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't add the school plan.");
    }

    public Task<IReadOnlyList<AgencySummary>> GetAgenciesAsync(Guid profileId)
        => GetAsync<IReadOnlyList<AgencySummary>>(
            $"api/care-profiles/{profileId}/agencies");

    public Task<IReadOnlyList<string>> GetAgencyKindsAsync(Guid profileId)
        => GetAsync<IReadOnlyList<string>>(
            $"api/care-profiles/{profileId}/agencies/kinds");

    public async Task AddAgencyAsync(
        Guid profileId, string name, string kind, string? contactName, string? phone)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"api/care-profiles/{profileId}/agencies")
        {
            Content = JsonContent.Create(
                new { name, kind, contactName, phone }, options: Json)
        });
        if (!resp.IsSuccessStatusCode)
            throw new ApiException("Couldn't add the agency.");
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
