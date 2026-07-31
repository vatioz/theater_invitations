using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceDeliveryEnvelopesWithRawTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProtectedDeliveryEnvelopes");

            migrationBuilder.AddColumn<string>(
                name: "RawToken",
                table: "RsvpTokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawToken",
                table: "RsvpTokens");

            migrationBuilder.CreateTable(
                name: "ProtectedDeliveryEnvelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProtectionPurpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "IX_ProtectedDeliveryEnvelopes_PartyId",
                table: "ProtectedDeliveryEnvelopes",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProtectedDeliveryEnvelopes_TokenId",
                table: "ProtectedDeliveryEnvelopes",
                column: "TokenId",
                unique: true);
        }
    }
}
