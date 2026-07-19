using CareTrack.Application;
using Microsoft.AspNetCore.Diagnostics;

namespace CareTrack.Api;

/// <summary>
/// Maps expected service-layer exceptions to HTTP status codes so they never
/// surface as 500s:
///
/// - <see cref="AccessDeniedException"/> → 404. The route-level authorization
///   handler already returns 403 when the caller lacks a grant on the profile;
///   by the time a service throws, the caller passed that check and the denial
///   means an entity id doesn't belong to the route's profile. 404 avoids
///   confirming that the entity exists elsewhere.
/// - <see cref="ArgumentException"/> → 400 with the validation message
///   (e.g. an appointment whose end precedes its start).
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, error) = exception switch
        {
            AccessDeniedException => (StatusCodes.Status404NotFound, "Not found."),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
            _ => (0, "")
        };
        if (status == 0) return false; // unexpected exception: keep the 500 path

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error }, ct);
        return true;
    }
}
