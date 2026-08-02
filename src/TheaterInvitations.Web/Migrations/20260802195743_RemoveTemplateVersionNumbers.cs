using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTemplateVersionNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Type_VersionNumber",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "TemplateVersionNumber",
                table: "EmailCampaigns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "EmailTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersionNumber",
                table: "EmailCampaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Type_VersionNumber",
                table: "EmailTemplates",
                columns: new[] { "Type", "VersionNumber" },
                unique: true);
        }
    }
}
