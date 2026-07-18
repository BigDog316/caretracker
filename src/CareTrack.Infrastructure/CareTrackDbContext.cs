using CareTrack.Domain;
using CareTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CareTrack.Infrastructure;

public class CareTrackDbContext
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public CareTrackDbContext(DbContextOptions<CareTrackDbContext> options)
        : base(options) { }

    // Intentionally hides IdentityUserContext.Users (DbSet<AppUser>): this
    // context exposes the domain User table; Identity accesses AppUser via
    // its own stores (Set<AppUser>()).
    public new DbSet<User> Users => Set<User>();
    public DbSet<CareProfile> CareProfiles => Set<CareProfile>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Identity table mappings

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        });

        b.Entity<CareProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        });

        b.Entity<AccessGrant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.User)
                .WithMany(u => u.AccessGrants)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.CareProfile)
                .WithMany(p => p.AccessGrants)
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // One active grant per (user, profile) pair. Revoked grants
            // (RevokedAt not null) are excluded so history can accumulate.
            e.HasIndex(x => new { x.UserId, x.CareProfileId })
                .IsUnique()
                .HasFilter("\"RevokedAt\" IS NULL");

            // Scoping queries hit these columns constantly.
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.CareProfileId);
        });

        b.Entity<Provider>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Organization).HasMaxLength(200);
            e.Property(x => x.Specialty).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);

            e.HasOne(x => x.CareProfile)
                .WithMany()
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CareProfileId);
        });

        b.Entity<Appointment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Location).HasMaxLength(300);

            e.HasOne(x => x.CareProfile)
                .WithMany()
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Provider)
                .WithMany(p => p.Appointments)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.SetNull);

            // The follow-up queue filters on profile + ends + not-yet-completed.
            e.HasIndex(x => new { x.CareProfileId, x.EndsAt });
            e.HasIndex(x => x.FollowUpCompletedAt);
        });

        b.Entity<Note>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).IsRequired();

            e.HasOne(x => x.CareProfile)
                .WithMany()
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Appointment)
                .WithMany(a => a.Notes)
                .HasForeignKey(x => x.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Provider)
                .WithMany(p => p.Notes)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.CareProfileId);
            e.HasIndex(x => x.ProviderId);
            e.HasIndex(x => x.AppointmentId);

            // Full-text search over note bodies uses a GIN index on a tsvector
            // computed at query time via EF.Functions.ToTsVector (see
            // NoteSearchService). Index the expression so those queries are fast.
            // Configured as a raw index in the migration; here we index Body to
            // support trigram/prefix fallback on providers without FTS.
        });

        b.Entity<Document>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(400);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(150);
            e.Property(x => x.StorageKey).IsRequired().HasMaxLength(400);

            e.HasOne(x => x.CareProfile)
                .WithMany()
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CareProfileId);
        });

        b.Entity<DocumentTag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Value).IsRequired().HasMaxLength(100);

            e.HasOne(x => x.Document)
                .WithMany(d => d.Tags)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.DocumentId, x.Value });
            e.HasIndex(x => x.Value);
        });

        b.Entity<Card>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Section).IsRequired().HasMaxLength(100);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(150);
            e.Property(x => x.StorageKey).IsRequired().HasMaxLength(400);

            e.HasOne(x => x.CareProfile)
                .WithMany()
                .HasForeignKey(x => x.CareProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.CareProfileId, x.Section });
        });
    }
}
