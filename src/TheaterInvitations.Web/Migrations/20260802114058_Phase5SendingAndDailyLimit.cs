using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SendingAndDailyLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimId",
                table: "EmailDispatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedAtUtc",
                table: "EmailDispatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContinueAfterUtc",
                table: "EmailCampaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAtUtc",
                table: "EmailCampaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailDailyAllowances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDailyAllowances", x => x.Id);
                });

            migrationBuilder.Sql("INSERT INTO \"EmailDailyAllowances\" (\"Id\", \"DayUtc\", \"ReservedCount\") SELECT md5((\"AcceptedAtUtc\" AT TIME ZONE 'UTC')::date::text)::uuid, (\"AcceptedAtUtc\" AT TIME ZONE 'UTC')::date, COUNT(*)::integer FROM \"EmailDispatches\" WHERE \"AcceptedAtUtc\" IS NOT NULL GROUP BY (\"AcceptedAtUtc\" AT TIME ZONE 'UTC')::date;");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDispatches_ClaimId",
                table: "EmailDispatches",
                column: "ClaimId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDailyAllowances_DayUtc",
                table: "EmailDailyAllowances",
                column: "DayUtc",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDailyAllowances");

            migrationBuilder.DropIndex(
                name: "IX_EmailDispatches_ClaimId",
                table: "EmailDispatches");

            migrationBuilder.DropColumn(
                name: "ClaimId",
                table: "EmailDispatches");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "EmailDispatches");

            migrationBuilder.DropColumn(
                name: "ContinueAfterUtc",
                table: "EmailCampaigns");

            migrationBuilder.DropColumn(
                name: "PausedAtUtc",
                table: "EmailCampaigns");
        }
    }
}
