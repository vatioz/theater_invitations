using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignResendRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceCampaignId",
                table: "EmailCampaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailCampaignSkips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReasonCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaignSkips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCampaignSkips_EmailCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "EmailCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailCampaignSkips_InvitationParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "InvitationParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReasonCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSuppressions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_SourceCampaignId",
                table: "EmailCampaigns",
                column: "SourceCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignSkips_CampaignId_PartyId_ReasonCategory",
                table: "EmailCampaignSkips",
                columns: new[] { "CampaignId", "PartyId", "ReasonCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignSkips_PartyId",
                table: "EmailCampaignSkips",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSuppressions_NormalizedEmail",
                table: "EmailSuppressions",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailCampaigns_EmailCampaigns_SourceCampaignId",
                table: "EmailCampaigns",
                column: "SourceCampaignId",
                principalTable: "EmailCampaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailCampaigns_EmailCampaigns_SourceCampaignId",
                table: "EmailCampaigns");

            migrationBuilder.DropTable(
                name: "EmailCampaignSkips");

            migrationBuilder.DropTable(
                name: "EmailSuppressions");

            migrationBuilder.DropIndex(
                name: "IX_EmailCampaigns_SourceCampaignId",
                table: "EmailCampaigns");

            migrationBuilder.DropColumn(
                name: "SourceCampaignId",
                table: "EmailCampaigns");
        }
    }
}
