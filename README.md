# CareTrack — Milestone 1: Domain + Access Model

This is the first milestone of the CareTrack build: the domain model and the
**access-control spine**, with a passing test suite proving that a user cannot
reach a care profile they have not been granted access to.

Everything else in the app (providers, appointments, notes, documents, cards,
school plans, agencies) hangs off `CareProfile` and is reachable only through
the access checks proven here. Get this right first; build features on top.

## Projects

| Project | Purpose |
|---|---|
| `CareTrack.Domain` | Entities: `User`, `CareProfile`, `AccessGrant`, `AccessRole`. No dependencies. |
| `CareTrack.Application` | `CareProfileAccessService` (the chokepoint), `IAccessGrantStore`, `AccessDeniedException`. |
| `CareTrack.Infrastructure` | EF Core `CareTrackDbContext` (PostgreSQL) and `EfAccessGrantStore`. |
| `CareTrack.Tests` | xUnit tests proving access scoping, including the requirements sharing scenario. |

## The access model

`AccessGrant` is a first-class join entity linking a `User` to a `CareProfile`
with a `Role` (Viewer < Editor < Owner). Access is many-to-many, so the same
profile can be shared with several users, and one user can hold many profiles.

The requirements scenario, encoded directly in the tests:

```
Caregiver A -> ChildA, ChildB, AdultC
Caregiver B -> ChildA, ChildD
Grandmother -> AdultC
```

Rules enforced:
- No grant → no access (throws `AccessDeniedException`).
- Role is hierarchical: an Owner satisfies an Editor or Viewer requirement; a
  Viewer does not satisfy an Editor requirement.
- Revoked grants (`RevokedAt` set) are ignored everywhere, including list queries.
- List/search must be scoped via `AccessibleProfileIdsAsync`, never "return all".

Every future service that touches care data must call
`CareProfileAccessService.RequireAccessAsync(userId, profileId, requiredRole)`
before reading or writing. That is the one place the rule lives.

## Run the tests

```bash
dotnet test
```

Expected: all tests in `CareProfileAccessTests` pass.

## Milestone 2: API, JWT auth, and access policies (this milestone)

`CareTrack.Api` adds the web layer:

- **DI wiring** (`AddCareTrackPersistence`, `AddCareTrackAuth`) registers the
  DbContext, the grant store, the access service, JWT bearer auth, and the
  authorization policies.
- **JWT** issuance (`JwtTokenService`) and validation. Signing key comes from the
  `Jwt:SigningKey` config; supply it from a secret store in production.
- **`ICurrentUser`** resolves the caller's id from the token's
  `sub`/`NameIdentifier` claim without leaking `HttpContext` into the app layer.
- **Authorization policies** (`CareProfile.Viewer/Editor/Owner`) backed by
  `CareProfileAccessHandler`, which pulls the `{careProfileId}` route value and
  calls the same access service proven in milestone 1. The one authorization
  rule still lives in exactly one place.
- **`[RequireCareProfile(AccessRole.Editor)]`** attribute for readable endpoints.
  See `CareProfilesController` for the pattern every feature controller follows.

The handler is unit-tested in `CareProfileAccessHandlerTests`: it grants on
sufficient role, denies on insufficient role, no grant, unauthenticated calls,
and missing/invalid route ids (fails closed on every edge).

### Running the API

```bash
# 1. Start Postgres (any local instance); set the connection string:
#    ConnectionStrings:CareTrackDb  in appsettings, or env override.

# 2. Create the initial migration (run from repo root):
dotnet ef migrations add InitialCreate \
  -p src/CareTrack.Infrastructure \
  -s src/CareTrack.Api

# 3. Apply it:
dotnet ef database update -s src/CareTrack.Api

# 4. Run:
dotnet run --project src/CareTrack.Api
```

`DesignTimeDbContextFactory` lets the `ef` commands build the context without
booting the web host (override the DB with the `CARETRACK_DB` env var).

> Install the EF tools once if needed: `dotnet tool install --global dotnet-ef`.

### Route convention

Any endpoint acting on a specific profile must include a `{careProfileId:guid}`
route segment and carry the matching `[RequireCareProfile(...)]` attribute, e.g.
`GET /api/care-profiles/{careProfileId}/providers`. List endpoints that span
profiles must scope through `AccessibleProfileIdsAsync` instead.

## Milestone 3: Providers + Appointments + Notes (this milestone)

The first feature slice — the core loop — built entirely on the access service.

**Domain:** `Provider`, `Appointment`, `Note`. A note always belongs to a profile
and may additionally attach to an appointment or provider. An appointment tracks
`FollowUpCompletedAt`; while null and past its end time it is "awaiting follow-up".

**Application services** (each enforces access before touching data):
- `ProviderService` — add/list/get. Address and phone are stored dialable/mappable
  for client deep-linking.
- `AppointmentService` — create (with calendar sync + validation) and
  `CompleteFollowUpAsync`, which records the caregiver's note and clears the prompt.
- `NoteService` — create, keyword search, and provider-filtered history (for the
  view/print/download feature).
- `FollowUpReminderService` — the **"How did it go?"** queue: ended appointments
  with no follow-up, scoped to exactly the profiles the caller can access.

**Abstractions:** `ICalendarSync` (with a `NoOpCalendarSync` default until a user
connects Google/Apple) and `IClock` (so follow-up timing is deterministic in tests).

**API endpoints** (all under the profile route, all policy-protected):
- `GET/POST /api/care-profiles/{careProfileId}/providers`
- `GET/POST /api/care-profiles/{careProfileId}/appointments`
- `POST /api/care-profiles/{careProfileId}/appointments/{appointmentId}/follow-up`
- `GET/POST /api/care-profiles/{careProfileId}/notes` (`?q=` to search)
- `GET /api/care-profiles/{careProfileId}/notes/history?providerId=...`
- `GET /api/reminders/follow-ups` (scoped internally to the caller's profiles)

**Tests** (`FeatureSliceTests`): outsiders can't write, Viewers can't create
appointments, the follow-up queue surfaces only ended un-actioned appointments and
never leaks across users, completing a follow-up records the note and clears the
prompt, note search is scoped and keyword-matched, and provider history filters
correctly.

> Note on search: `EfCareDataRepository.SearchNotesAsync` uses PostgreSQL full-text
> search (`to_tsvector`/`plainto_tsquery`). For large datasets, add a GIN index on
> `to_tsvector('english', "Body")` in a migration to keep it fast. Tests use a
> substring fallback so they run without Postgres.

## Milestone 4: Identity + auth endpoints (this milestone)

Real ASP.NET Core Identity replaces the token stub.

- `CareTrackDbContext` now derives from `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`.
- `AppUser : IdentityUser<Guid>` is the auth identity; it shares its primary key
  with the domain `User`, so the JWT subject claim is directly the domain user id
  used in access checks. Registration creates both records in one transaction.
- `IAuthService` (implemented by `IdentityAuthService`) provides register, login,
  refresh-token rotation, and logout. Access tokens are short-lived JWTs (15 min);
  refresh tokens are persisted, rotated on use, and revocable.
- `IAccessTokenIssuer` / `JwtAccessTokenIssuer` moved token creation into
  Infrastructure so the auth service can issue tokens without an Api dependency.

Endpoints (all `AllowAnonymous` except logout):
- `POST /api/auth/register`  → `{ userId, accessToken, refreshToken, accessTokenExpiresAt }`
- `POST /api/auth/login`
- `POST /api/auth/refresh`   (rotates the refresh token)
- `POST /api/auth/logout`    (revokes a refresh token; requires auth)

Password policy and lockout are configured in `AddCareTrackAuth`. `RefreshTokenTests`
pins the token-validity logic; the full flow needs an integration test against a real
database because Identity's `UserManager` and password hashing require the EF stores.

> Security note: refresh tokens are stored raw here for brevity. Before shipping,
> store a hash and look up by hash, and serve tokens over HTTPS only.

## Milestone 5: Documents + Cards (this milestone)

- `Document` (+ `DocumentTag`) and `Card` entities. File bytes live in blob
  storage; only metadata rows sit in the database, pointing at a `StorageKey`.
- `IDocumentStore` abstraction with a `LocalDiskDocumentStore` dev implementation
  (path-traversal guarded, partitioned by profile id). Swap in S3/Azure Blob for
  production without touching callers.
- `DocumentService` and `CardService` coordinate storage + metadata, access-scoped
  on every operation (Editor to upload/delete, Viewer to list/download).
- Cards use user-definable sections with sensible defaults (`CardSections.Defaults`:
  Insurance, Provider Business Card, Appointment Reminder, Membership, Other).
- Both documents and cards carry an accessibility `Description` for the uploaded
  image/file, supporting the WCAG alt-text requirement.

Endpoints (profile-scoped, policy-protected):
- `GET/POST /api/care-profiles/{careProfileId}/documents` (multipart upload; `?tag=` filter)
- `GET /api/care-profiles/{careProfileId}/documents/{documentId}/content` (download)
- `DELETE /api/care-profiles/{careProfileId}/documents/{documentId}`
- `GET /api/care-profiles/{careProfileId}/cards/sections`
- `GET/POST /api/care-profiles/{careProfileId}/cards` (multipart; `?section=` filter)
- `GET /api/care-profiles/{careProfileId}/cards/{cardId}/image`
- `DELETE /api/care-profiles/{careProfileId}/cards/{cardId}`

`DocumentAndCardTests` cover upload storing blob + metadata + tags, outsider denial,
tag/section filtering, delete removing both metadata and blob, and image download.

## Next milestone

1. Concrete `ICalendarSync` for Google and Apple + the .ics fallback download.
2. Background delivery of "How did it go?" reminders (push/email) over the queue.
3. School (IEP/504) and Agencies sections, reusing the document + calendar patterns.
4. Integration tests via `WebApplicationFactory` against a test Postgres, covering
   the full auth flow and an end-to-end access-denied path through the HTTP layer.
5. The Blazor WASM web client and the .NET MAUI Blazor Hybrid mobile client on top
   of this API, sharing a component library, built to the WCAG 2.2 AA bar.
