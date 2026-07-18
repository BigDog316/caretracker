using Microsoft.AspNetCore.Identity;

namespace CareTrack.Infrastructure.Identity;

/// <summary>
/// The authentication identity, managed by ASP.NET Core Identity (password hash,
/// lockout, email confirmation, etc.). It shares its primary key with the
/// domain <see cref="Domain.User"/>: the same Guid identifies both, so the JWT
/// subject claim (the AppUser id) is directly usable as the domain user id in
/// access checks. Registration creates both records under one transaction.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
