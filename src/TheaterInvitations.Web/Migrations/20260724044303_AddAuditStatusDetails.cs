using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditStatusDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviousStatus",
                table: "AuditEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedStatus",
                table: "AuditEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultingStatus",
                table: "AuditEvents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "RequestedStatus",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ResultingStatus",
                table: "AuditEvents");
        }
    }
}
