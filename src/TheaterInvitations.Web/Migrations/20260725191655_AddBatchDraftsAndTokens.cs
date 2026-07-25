using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchDraftsAndTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpirationSource",
                table: "InvitationParties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommittedAtUtc",
                table: "InvitationBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommittedBy",
                table: "InvitationBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "InvitationBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                table: "InvitationBatches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "InvitationBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDigest",
                table: "InvitationBatches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "InvitationBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ValidationIssue",
                table: "InvitationBatches",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvitationDraftRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "integer", nullable: false),
                    PrimaryGuestName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllocatedSeats = table.Column<int>(type: "integer", nullable: true),
                    ValidationIssue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationDraftRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationDraftRows_InvitationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "InvitationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RsvpTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReasonCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RsvpTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RsvpTokens_InvitationParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "InvitationParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProtectedDeliveryEnvelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProtectionPurpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProtectedDeliveryEnvelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProtectedDeliveryEnvelopes_InvitationParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "InvitationParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProtectedDeliveryEnvelopes_RsvpTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "RsvpTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDraftRows_BatchId_SourceRowNumber",
                table: "InvitationDraftRows",
                columns: new[] { "BatchId", "SourceRowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProtectedDeliveryEnvelopes_PartyId",
                table: "ProtectedDeliveryEnvelopes",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProtectedDeliveryEnvelopes_TokenId",
                table: "ProtectedDeliveryEnvelopes",
                column: "TokenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RsvpTokens_Hash",
                table: "RsvpTokens",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RsvpTokens_PartyId",
                table: "RsvpTokens",
                column: "PartyId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvitationDraftRows");

            migrationBuilder.DropTable(
                name: "ProtectedDeliveryEnvelopes");

            migrationBuilder.DropTable(
                name: "RsvpTokens");

            migrationBuilder.DropColumn(
                name: "ExpirationSource",
                table: "InvitationParties");

            migrationBuilder.DropColumn(
                name: "CommittedAtUtc",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "CommittedBy",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "SourceDigest",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "State",
                table: "InvitationBatches");

            migrationBuilder.DropColumn(
                name: "ValidationIssue",
                table: "InvitationBatches");
        }
    }
}
