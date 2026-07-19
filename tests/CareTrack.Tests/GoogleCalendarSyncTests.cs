using System.Net;
using System.Text.Json;
using CareTrack.Domain;
using CareTrack.Infrastructure.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Google Calendar sync over a faked HTTP transport: correct token + event
/// requests, graceful no-connection behavior, and stale-grant pruning.
/// </summary>
public class GoogleCalendarSyncTests
{
    private sealed class FakeConnectionStore : IGoogleCalendarConnectionStore
    {
        public Dictionary<Guid, string> Tokens { get; } = new();

        public Task<GoogleCalendarConnection?> GetAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Tokens.TryGetValue(userId, out var t)
                ? new GoogleCalendarConnection { UserId = userId, RefreshToken = t }
                : null);

        public Task UpsertAsync(Guid userId, string refreshToken, CancellationToken ct = default)
        { Tokens[userId] = refreshToken; return Task.CompletedTask; }

        public Task RemoveAsync(Guid userId, CancellationToken ct = default)
        { Tokens.Remove(userId); return Task.CompletedTask; }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = new();
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode EventStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null
                ? "" : await request.Content.ReadAsStringAsync(ct);
            var url = request.RequestUri!.ToString();
            Requests.Add((url, body));

            if (url.Contains("oauth2.googleapis.com/token"))
                return new HttpResponseMessage(TokenStatus)
                {
                    Content = new StringContent("{\"access_token\":\"at-123\"}")
                };
            return new HttpResponseMessage(EventStatus)
            {
                Content = new StringContent("{\"id\":\"evt-42\"}")
            };
        }
    }

    private readonly Guid _user = Guid.NewGuid();
    private readonly FakeConnectionStore _store = new();
    private readonly FakeHandler _handler = new();

    private GoogleCalendarSync Sync() => new(
        new HttpClient(_handler),
        _store,
        Options.Create(new GoogleCalendarOptions
        { ClientId = "cid", ClientSecret = "secret" }),
        NullLogger<GoogleCalendarSync>.Instance);

    private static Appointment MakeAppointment() => new()
    {
        CareProfileId = Guid.NewGuid(),
        Title = "Cardiology check",
        Location = "Suite 4",
        StartsAt = DateTimeOffset.Parse("2026-02-01T14:30:00Z"),
        EndsAt = DateTimeOffset.Parse("2026-02-01T15:00:00Z")
    };

    [Fact]
    public async Task No_connection_means_no_sync_and_no_http_calls()
    {
        var id = await Sync().CreateEventAsync(_user, MakeAppointment());
        Assert.Null(id);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Connected_user_gets_event_created_with_utc_times()
    {
        _store.Tokens[_user] = "refresh-abc";

        var id = await Sync().CreateEventAsync(_user, MakeAppointment());

        Assert.Equal("evt-42", id);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains("refresh_token=refresh-abc", _handler.Requests[0].Body);

        var evt = JsonSerializer.Deserialize<JsonElement>(_handler.Requests[1].Body);
        Assert.Equal("Cardiology check", evt.GetProperty("summary").GetString());
        Assert.Equal("Suite 4", evt.GetProperty("location").GetString());
        Assert.StartsWith("2026-02-01T14:30:00",
            evt.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("UTC",
            evt.GetProperty("end").GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Failed_token_refresh_prunes_the_stale_connection()
    {
        _store.Tokens[_user] = "revoked-token";
        _handler.TokenStatus = HttpStatusCode.BadRequest;

        var id = await Sync().CreateEventAsync(_user, MakeAppointment());

        Assert.Null(id);
        Assert.Empty(_store.Tokens); // connection removed; user must reconnect
    }

    [Fact]
    public async Task Failed_event_create_returns_null_but_keeps_connection()
    {
        _store.Tokens[_user] = "refresh-abc";
        _handler.EventStatus = HttpStatusCode.Forbidden;

        var id = await Sync().CreateEventAsync(_user, MakeAppointment());

        Assert.Null(id);
        Assert.Single(_store.Tokens);
    }

    [Fact]
    public async Task Cancel_deletes_the_external_event()
    {
        _store.Tokens[_user] = "refresh-abc";

        await Sync().CancelEventAsync(_user, "evt-42");

        Assert.Equal(2, _handler.Requests.Count);
        Assert.EndsWith("/events/evt-42", _handler.Requests[1].Url);
    }
}
