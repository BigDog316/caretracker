# CareTrack — Handoff for Claude Code

This solution was written and verified by inspection but **never compiled**. Your
first job is to get it building and green, then continue the milestones. Work in a
real red/green loop: build, run tests, fix, repeat.

## Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

The API startup project is `src/CareTrack.Api`. Tests are in
`tests/CareTrack.Tests` (xUnit).

## Guardrails — do NOT change these while fixing the build

These are the load-bearing design decisions. A compile-fixing pass must not quietly
weaken them to make an error disappear.

1. **The access-control model.** `AccessGrant` is the security spine. Every read or
   write of care data routes through `CareProfileAccessService.RequireAccessAsync`
   / `HasAccessAsync`. Do not bypass it, do not add a "return all rows" path, do not
   loosen the role checks (Owner ≥ Editor ≥ Viewer), and do not honor revoked grants.
   If a test in `CareProfileAccessTests` or `FeatureSliceTests` fails, fix the code
   so the test's intent holds — do not weaken the test.
2. **Layer boundaries.** `CareTrack.Domain` references nothing infrastructural
   (no EF, Identity, Npgsql, or ASP.NET). `CareTrack.Application` references only
   Domain (no EF/Identity/ASP.NET). Infrastructure and Api hold all the framework
   dependencies. Don't collapse a project reference to resolve a namespace error;
   move the code to the correct layer instead.
3. **Shared user id.** `AppUser : IdentityUser<Guid>` and the domain `User` share one
   primary key on purpose, so the JWT subject claim is directly the domain user id.
   Keep them in sync; registration creates both in one transaction.

If you believe one of these genuinely must change, stop and flag it rather than
editing silently.

## Known first-compile friction (expected — not design problems)

- **NuGet version pins.** Package versions are pinned to a .NET 8 baseline. If they
  don't resolve against the installed SDK, bump them to compatible versions rather
  than changing the code. Keep all `Microsoft.EntityFrameworkCore.*`,
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and `Npgsql.EntityFrameworkCore.PostgreSQL`
  versions aligned to the same major/minor.
- **No EF migration exists yet.** Generate the initial migration against a real
  Postgres (see below). The `DesignTimeDbContextFactory` lets `dotnet ef` build the
  context without booting the web host.
- **PostgreSQL full-text search.** `EfCareDataRepository.SearchNotesAsync` uses
  `EF.Functions.ToTsVector` / `PlainToTsQuery`. This requires the Npgsql provider and
  a running Postgres; it won't work on other providers. Tests use an in-memory
  substring fallback, so unit tests don't need a database. Consider adding a GIN index
  on `to_tsvector('english', "Body")` in the migration for performance.
- **Identity tables.** Because the DbContext is now an `IdentityDbContext`, the initial
  migration will include the ASP.NET Identity tables plus `RefreshTokens`.

## Database setup (for migrations / running the API, not for unit tests)

```bash
# Any local Postgres. Default dev connection string is in appsettings.json:
#   Host=localhost;Database=caretrack;Username=postgres;Password=postgres
# Override for ef commands with the CARETRACK_DB env var if needed.

dotnet tool install --global dotnet-ef   # once, if not installed

dotnet ef migrations add InitialCreate \
  -p src/CareTrack.Infrastructure \
  -s src/CareTrack.Api

dotnet ef database update -s src/CareTrack.Api

dotnet run --project src/CareTrack.Api
```

## Dev-grade items to harden before shipping (flagged in code)

- **Refresh tokens are stored raw** in `RefreshToken.Token`. Store a hash and look up
  by hash. Serve over HTTPS only.
- **`LocalDiskDocumentStore` is dev-only.** Provide an S3/Azure Blob implementation of
  `IDocumentStore` for production; callers don't change.
- **JWT signing key** in `appsettings.json` is a dev placeholder. Load from a secret
  store / environment variable in real environments.

## What exists now (milestones 1–5)

- **Domain:** User, CareProfile, AccessGrant (+ AccessRole), Provider, Appointment,
  Note, Document (+ DocumentTag), Card.
- **Application:** access service, feature services (Provider, Appointment, Note,
  FollowUpReminder, Document, Card), auth surface (`IAuthService`), and abstractions
  (`ICareDataRepository`, `IDocumentStore`, `ICalendarSync`, `IClock`,
  `IAccessTokenIssuer`, `ICurrentUser`).
- **Infrastructure:** EF Core DbContext (Identity + care data), EF repository + grant
  store, Identity (`AppUser`, `RefreshToken`, `IdentityAuthService`, JWT issuer),
  local-disk document store.
- **Api:** JWT auth + policy handler resolving `{careProfileId}` route access, and
  controllers for auth, care-profiles, providers, appointments, notes, reminders,
  documents, cards.
- **Tests:** access scoping, the authorization handler, the Providers/Appointments/
  Notes feature slice incl. the "How did it go?" follow-up queue, refresh-token
  validity, and the documents/cards flow.

## Next milestones (in order)

1. **Integration tests** via `WebApplicationFactory` against a test Postgres
   (Testcontainers works well): full register → login → refresh flow, and an
   end-to-end access-denied path through the HTTP layer (user without a grant gets
   403 on a profile-scoped route).
2. **Calendar sync:** concrete `ICalendarSync` for Google and Apple, plus the `.ics`
   download fallback. Wire into `AppointmentService` (already calls `CreateEventAsync`).
3. **Reminder delivery:** background job (hosted service) that turns the
   `FollowUpReminderService` queue into push/email "How did it go?" prompts.
4. **School (IEP/504) and Agencies** sections, reusing the document + calendar patterns.
5. **Clients:** Blazor WebAssembly web app and .NET MAUI Blazor Hybrid mobile app on
   top of this API, sharing a component library, built to WCAG 2.2 AA (see the build
   prompt / requirements for the accessibility bar).

## Working style

Keep changes small and test-backed. After each milestone, `dotnet test` should be
green before moving on. Prefer fixing the code to match a failing test over changing
the test, unless the test itself is demonstrably wrong — and if it is, say so.
