using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyPriorityAndPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "InvitationParties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "InvitationParties",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "InvitationDraftRows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "InvitationDraftRows",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvitationParties_Priority",
                table: "InvitationParties",
                sql: "\"Priority\" BETWEEN 1 AND 3");

            // Preserve every existing batch while making legacy case-insensitive duplicates unique.
            migrationBuilder.Sql("""
                WITH duplicates AS (
                    SELECT "Id", "Name",
                           ROW_NUMBER() OVER (PARTITION BY LOWER("Name") ORDER BY "CreatedAtUtc", "Id") AS duplicate_number
                    FROM "InvitationBatches"
                )
                UPDATE "InvitationBatches" AS batches
                SET "Name" = LEFT(duplicates."Name", 150) || ' [legacy-' || REPLACE(duplicates."Id"::text, '-', '') || ']'
                FROM duplicates
                WHERE batches."Id" = duplicates."Id" AND duplicates.duplicate_number > 1;
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_InvitationParties_Email_CaseInsensitive\" ON \"InvitationParties\" (LOWER(\"Email\"));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_InvitationBatches_Name_CaseInsensitive\" ON \"InvitationBatches\" (LOWER(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InvitationParties_Priority",
                table: "InvitationParties");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_InvitationParties_Email_CaseInsensitive\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_InvitationBatches_Name_CaseInsensitive\";");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "InvitationParties");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "InvitationParties");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "InvitationDraftRows");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "InvitationDraftRows");
        }
    }
}
