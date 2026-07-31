using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmailCampaignId",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmailDispatchId",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailSenderConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReplyToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DailySendCeiling = table.Column<int>(type: "integer", nullable: false),
                    IsDomainVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSenderConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    PlainTextBody = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    TemplateDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FromDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReplyToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QueuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCampaigns_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailCampaigns_InvitationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "InvitationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllocatedSeats = table.Column<int>(type: "integer", nullable: false),
                    DeadlineUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailDispatches_EmailCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "EmailCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailDispatches_InvitationParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "InvitationParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailDispatches_RsvpTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "RsvpTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_BatchId",
                table: "EmailCampaigns",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_TemplateId",
                table: "EmailCampaigns",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDispatches_CampaignId_PartyId",
                table: "EmailDispatches",
                columns: new[] { "CampaignId", "PartyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDispatches_IdempotencyKey",
                table: "EmailDispatches",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDispatches_PartyId",
                table: "EmailDispatches",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDispatches_TokenId",
                table: "EmailDispatches",
                column: "TokenId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Type_VersionNumber",
                table: "EmailTemplates",
                columns: new[] { "Type", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDispatches");

            migrationBuilder.DropTable(
                name: "EmailSenderConfigurations");

            migrationBuilder.DropTable(
                name: "EmailCampaigns");

            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "EmailCampaignId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "EmailDispatchId",
                table: "AuditEvents");
        }
    }
}
