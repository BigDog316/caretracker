namespace CareTrack.Domain;

/// <summary>
/// The level of access a <see cref="User"/> has to a <see cref="CareProfile"/>,
/// as recorded on an <see cref="AccessGrant"/>.
/// Ordered so that a numeric comparison expresses capability:
/// Owner (2) &gt; Editor (1) &gt; Viewer (0).
/// </summary>
public enum AccessRole
{
    /// <summary>Read-only access. Cannot modify data or manage sharing.</summary>
    Viewer = 0,

    /// <summary>Can read and write care data, but cannot manage sharing/grants.</summary>
    Editor = 1,

    /// <summary>Full control, including granting and revoking access for others.</summary>
    Owner = 2
}
