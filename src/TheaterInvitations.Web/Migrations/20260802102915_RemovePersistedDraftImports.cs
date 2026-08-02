using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemovePersistedDraftImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy drafts are not authoritative imports and cannot be promoted safely.
            migrationBuilder.Sql("DELETE FROM \"InvitationBatches\" WHERE \"State\" IN (1, 2);");
            migrationBuilder.DropTable(
                name: "InvitationDraftRows");

            migrationBuilder.DropColumn(
                name: "ValidationIssue",
                table: "InvitationBatches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                    AllocatedSeats = table.Column<int>(type: "integer", nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PrimaryGuestName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: true),
                    SourceRowNumber = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDraftRows_BatchId_SourceRowNumber",
                table: "InvitationDraftRows",
                columns: new[] { "BatchId", "SourceRowNumber" },
                unique: true);
        }
    }
}
