namespace CareTrack.Infrastructure.Identity;

/// <summary>Bound from the "Jwt" configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "caretrack";
    public string Audience { get; set; } = "caretrack";

    /// <summary>
    /// Signing key. Supply from a secret store / environment variable in
    /// production, never from committed appsettings. At least 32 bytes.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
