namespace CareTrack.Application;

/// <summary>
/// Thrown when a user attempts to access a care profile they do not hold an
/// active grant for, or holds an insufficient role for the requested action.
/// The message deliberately does not reveal whether the profile exists, to
/// avoid leaking the existence of profiles to unauthorized users.
/// </summary>
public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException()
        : base("You do not have access to this care profile.") { }
}
