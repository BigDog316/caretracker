using CareTrack.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Boots the real API (Program) against a per-fixture PostgreSQL database that
/// is created, migrated, and dropped by the fixture. Requires a reachable
/// Postgres; point CARETRACK_TEST_DB at an admin connection string (database
/// must be one the user can connect to, e.g. "postgres") or rely on the
/// localhost default. Tests should guard with
/// <c>Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, ...)</c> so the suite
/// stays green on machines without a database.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("CARETRACK_TEST_DB")
        ?? "Host=localhost;Database=postgres;Username=postgres;Password=postgres;Timeout=3";

    private static readonly Lazy<bool> Availability = new(() =>
    {
        try
        {
            using var conn = new NpgsqlConnection(AdminConnectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsPostgresAvailable => Availability.Value;

    private readonly string _databaseName =
        $"caretrack_test_{Guid.NewGuid():N}";

    private string TestConnectionString
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Database = _databaseName
            };
            return b.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CareTrackDb", TestConnectionString);
        // The Development host loads the developer's user-secrets; blank out
        // external-service credentials so tests are hermetic no matter what
        // is configured on the machine.
        builder.UseSetting("GoogleCalendar:ClientId", "");
        builder.UseSetting("GoogleCalendar:ClientSecret", "");
    }

    public async Task InitializeAsync()
    {
        if (!IsPostgresAvailable) return; // tests will Skip before using us

        await using (var conn = new NpgsqlConnection(AdminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareTrackDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (!IsPostgresAvailable) return;

        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
    }
}
