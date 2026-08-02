using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class TemplateSenderDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FromDisplayName",
                table: "EmailTemplates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "EmailTemplates" AS template
                SET "FromDisplayName" = sender."FromDisplayName"
                FROM "EmailSenderConfigurations" AS sender
                WHERE template."FromDisplayName" IS NULL
                  AND NULLIF(BTRIM(sender."FromDisplayName"), '') IS NOT NULL;
                """);

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql("""
                UPDATE "EmailTemplates"
                SET "ContentDigest" = upper(encode(digest(
                    (CASE WHEN "Type" = 0 THEN 'InitialInvitation' ELSE 'Reminder' END) || E'\n' ||
                    COALESCE("FromDisplayName", '') || E'\n' || "Subject" || E'\n' || "HtmlBody" || E'\n' || "PlainTextBody",
                    'sha256'), 'hex'));
                """);

            migrationBuilder.Sql("""
                UPDATE "EmailCampaigns"
                SET "State" = 7,
                    "InvalidatedAtUtc" = CURRENT_TIMESTAMP,
                    "InvalidationReasonCategory" = 'template-sender-display-name-changed'
                WHERE "State" IN (1, 2, 3, 8);
                """);

            migrationBuilder.DropColumn(
                name: "FromDisplayName",
                table: "EmailSenderConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromDisplayName",
                table: "EmailTemplates");

            migrationBuilder.AddColumn<string>(
                name: "FromDisplayName",
                table: "EmailSenderConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
