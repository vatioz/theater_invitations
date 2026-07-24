using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Data;

public sealed class InvitationDbContext(DbContextOptions<InvitationDbContext> options) : DbContext(options)
{
    public DbSet<EventConfiguration> EventConfigurations => Set<EventConfiguration>();
    public DbSet<InvitationBatch> InvitationBatches => Set<InvitationBatch>();
    public DbSet<InvitationParty> InvitationParties => Set<InvitationParty>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventConfiguration>(entity =>
        {
            entity.Property(x => x.Capacity).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SupportEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<InvitationBatch>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<InvitationParty>(entity =>
        {
            entity.Property(x => x.PrimaryGuestName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Company).HasMaxLength(200);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AccessibilityRequirements).HasMaxLength(500);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_InvitationParties_AllocatedSeats", "\"AllocatedSeats\" > 0"));
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ActorCategory).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReasonCategory).HasMaxLength(100);
        });
    }
}
