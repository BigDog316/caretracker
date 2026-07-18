using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CareTrack.Api;

/// <summary>
/// Lets `dotnet ef migrations add ...` construct the DbContext at design time
/// without running the full web host. Uses a local connection string; override
/// via the CARETRACK_DB environment variable if your dev database differs.
/// </summary>
public sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<Infrastructure.CareTrackDbContext>
{
    public Infrastructure.CareTrackDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CARETRACK_DB")
                 ?? "Host=localhost;Database=caretrack;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<Infrastructure.CareTrackDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new Infrastructure.CareTrackDbContext(options);
    }
}
