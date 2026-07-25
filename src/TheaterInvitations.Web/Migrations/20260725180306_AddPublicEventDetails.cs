using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheaterInvitations.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicEventDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DoorsAtUtc",
                table: "EventConfigurations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "DressCode",
                table: "EventConfigurations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventName",
                table: "EventConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartsAtUtc",
                table: "EventConfigurations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "VenueAddress",
                table: "EventConfigurations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VenueName",
                table: "EventConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoorsAtUtc",
                table: "EventConfigurations");

            migrationBuilder.DropColumn(
                name: "DressCode",
                table: "EventConfigurations");

            migrationBuilder.DropColumn(
                name: "EventName",
                table: "EventConfigurations");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "EventConfigurations");

            migrationBuilder.DropColumn(
                name: "VenueAddress",
                table: "EventConfigurations");

            migrationBuilder.DropColumn(
                name: "VenueName",
                table: "EventConfigurations");
        }
    }
}
