using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ReviewSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve the meaning of persisted enum values while replacing the approval gate.
            migrationBuilder.Sql("UPDATE \"EmailTemplates\" SET \"State\" = 0 WHERE \"State\" IN (0, 1);");
            migrationBuilder.Sql("UPDATE \"EmailTemplates\" SET \"State\" = 1 WHERE \"State\" = 2;");
            migrationBuilder.Sql("UPDATE \"EmailCampaigns\" SET \"State\" = 7 WHERE \"State\" IN (1, 2, 3);");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "EmailTemplates");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvalidatedAtUtc",
                table: "EmailCampaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvalidationReasonCategory",
                table: "EmailCampaigns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewFingerprint",
                table: "EmailCampaigns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"EmailTemplates\" SET \"State\" = 2 WHERE \"State\" = 1;");
            migrationBuilder.Sql("UPDATE \"EmailCampaigns\" SET \"State\" = 1 WHERE \"State\" = 7;");

            migrationBuilder.DropColumn(
                name: "InvalidatedAtUtc",
                table: "EmailCampaigns");

            migrationBuilder.DropColumn(
                name: "InvalidationReasonCategory",
                table: "EmailCampaigns");

            migrationBuilder.DropColumn(
                name: "ReviewFingerprint",
                table: "EmailCampaigns");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                table: "EmailTemplates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "EmailTemplates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
