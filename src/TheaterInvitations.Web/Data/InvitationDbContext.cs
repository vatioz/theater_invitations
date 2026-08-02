using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Data;

public sealed class InvitationDbContext(DbContextOptions<InvitationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<EventConfiguration> EventConfigurations => Set<EventConfiguration>();
    public DbSet<InvitationBatch> InvitationBatches => Set<InvitationBatch>();
    public DbSet<InvitationParty> InvitationParties => Set<InvitationParty>();
    public DbSet<RsvpToken> RsvpTokens => Set<RsvpToken>();
    public DbSet<EmailSenderConfiguration> EmailSenderConfigurations => Set<EmailSenderConfiguration>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailCampaign> EmailCampaigns => Set<EmailCampaign>();
    public DbSet<EmailDispatch> EmailDispatches => Set<EmailDispatch>();
    public DbSet<EmailCampaignSkip> EmailCampaignSkips => Set<EmailCampaignSkip>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<EmailDailyAllowance> EmailDailyAllowances => Set<EmailDailyAllowance>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
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
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<InvitationParty>(entity =>
        {
            entity.Property(x => x.PrimaryGuestName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Company).HasMaxLength(200);
            entity.Property(x => x.Phone).HasMaxLength(64);
            entity.Property(x => x.Priority).HasDefaultValue(3);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AccessibilityRequirements).HasMaxLength(500);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_InvitationParties_AllocatedSeats", "\"AllocatedSeats\" > 0"));
            entity.ToTable(table => table.HasCheckConstraint("CK_InvitationParties_Priority", "\"Priority\" BETWEEN 1 AND 3"));
        });

        modelBuilder.Entity<RsvpToken>(entity =>
        {
            entity.Property(x => x.Hash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RawToken).HasMaxLength(128);
            entity.Property(x => x.RevocationReasonCategory).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Hash).IsUnique();
            entity.HasIndex(x => x.PartyId).HasFilter("\"RevokedAtUtc\" IS NULL").IsUnique();
            entity.HasOne<InvitationParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ActorCategory).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReasonCategory).HasMaxLength(100);
        });

        modelBuilder.Entity<EmailSenderConfiguration>(entity =>
        {
            entity.Property(x => x.FromDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ReplyToAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.VerifiedBy).HasMaxLength(200);
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContentDigest).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => new { x.Type, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<EmailCampaign>(entity =>
        {
            entity.Property(x => x.TemplateDigest).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FromDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ReplyToAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ReviewFingerprint).HasMaxLength(128).IsRequired();
            entity.Property(x => x.InvalidationReasonCategory).HasMaxLength(100);
            entity.Property(x => x.SourceCampaignId);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<InvitationBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmailTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmailCampaign>().WithMany().HasForeignKey(x => x.SourceCampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailCampaignSkip>(entity =>
        {
            entity.Property(x => x.ReasonCategory).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.CampaignId, x.PartyId, x.ReasonCategory }).IsUnique();
            entity.HasOne<EmailCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<InvitationParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailSuppression>(entity =>
        {
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ReasonCategory).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<EmailDispatch>(entity =>
        {
            entity.Property(x => x.RecipientEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ProviderMessageId).HasMaxLength(200);
            entity.Property(x => x.FailureCategory).HasMaxLength(100);
            entity.Property(x => x.ClaimId);
            entity.HasIndex(x => x.ClaimId).IsUnique();
            entity.HasIndex(x => new { x.CampaignId, x.PartyId }).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasOne<EmailCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<InvitationParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RsvpToken>().WithMany().HasForeignKey(x => x.TokenId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailDailyAllowance>(entity =>
        {
            entity.Property(x => x.DayUtc).HasColumnType("date");
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.DayUtc).IsUnique();
        });
    }
}
