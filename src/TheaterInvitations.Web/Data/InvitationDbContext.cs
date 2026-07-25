using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Data;

public sealed class InvitationDbContext(DbContextOptions<InvitationDbContext> options) : DbContext(options)
{
    public DbSet<EventConfiguration> EventConfigurations => Set<EventConfiguration>();
    public DbSet<InvitationBatch> InvitationBatches => Set<InvitationBatch>();
    public DbSet<InvitationParty> InvitationParties => Set<InvitationParty>();
    public DbSet<InvitationDraftRow> InvitationDraftRows => Set<InvitationDraftRow>();
    public DbSet<RsvpToken> RsvpTokens => Set<RsvpToken>();
    public DbSet<ProtectedDeliveryEnvelope> ProtectedDeliveryEnvelopes => Set<ProtectedDeliveryEnvelope>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventConfiguration>(entity =>
        {
            entity.Property(x => x.Capacity).IsRequired();
            entity.Property(x => x.EventName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VenueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VenueAddress).HasMaxLength(500).IsRequired();
            entity.Property(x => x.DressCode).HasMaxLength(500);
            entity.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SupportEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<InvitationBatch>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(200);
            entity.Property(x => x.ModifiedBy).HasMaxLength(200);
            entity.Property(x => x.CommittedBy).HasMaxLength(200);
            entity.Property(x => x.SourceDigest).HasMaxLength(128);
            entity.Property(x => x.ValidationIssue).HasMaxLength(2000);
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<InvitationParty>(entity =>
        {
            entity.Property(x => x.PrimaryGuestName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Company).HasMaxLength(200);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AccessibilityRequirements).HasMaxLength(500);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_InvitationParties_AllocatedSeats", "\"AllocatedSeats\" > 0"));
        });

        modelBuilder.Entity<InvitationDraftRow>(entity =>
        {
            entity.Property(x => x.PrimaryGuestName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Company).HasMaxLength(200);
            entity.Property(x => x.ValidationIssue).HasMaxLength(500);
            entity.HasIndex(x => new { x.BatchId, x.SourceRowNumber }).IsUnique();
            entity.HasOne<InvitationBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RsvpToken>(entity =>
        {
            entity.Property(x => x.Hash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RevocationReasonCategory).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Hash).IsUnique();
            entity.HasIndex(x => x.PartyId).HasFilter("\"RevokedAtUtc\" IS NULL").IsUnique();
            entity.HasOne<InvitationParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProtectedDeliveryEnvelope>(entity =>
        {
            entity.Property(x => x.ProtectedToken).IsRequired();
            entity.Property(x => x.ProtectionPurpose).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.TokenId).IsUnique();
            entity.HasOne<InvitationParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RsvpToken>().WithMany().HasForeignKey(x => x.TokenId).OnDelete(DeleteBehavior.Cascade);
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
