using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorIdentifier = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReasonCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SupportEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AccessibilityTextLimit = table.Column<int>(type: "integer", nullable: false),
                    IsRsvpLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvitationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeadlineUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvitationParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryGuestName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllocatedSeats = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AccessibilityRequirements = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RespondedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationParties", x => x.Id);
                    table.CheckConstraint("CK_InvitationParties_AllocatedSeats", "\"AllocatedSeats\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationParties_TokenHash",
                table: "InvitationParties",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "EventConfigurations");

            migrationBuilder.DropTable(
                name: "InvitationBatches");

            migrationBuilder.DropTable(
                name: "InvitationParties");
        }
    }
}
