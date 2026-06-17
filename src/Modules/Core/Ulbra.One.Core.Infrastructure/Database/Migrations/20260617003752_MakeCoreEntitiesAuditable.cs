using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Core.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class MakeCoreEntitiesAuditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by",
                schema: "core",
                table: "units",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "core",
                table: "units",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                schema: "core",
                table: "units",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                schema: "core",
                table: "cost_centers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "core",
                table: "cost_centers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                schema: "core",
                table: "cost_centers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                schema: "core",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "core",
                table: "companies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                schema: "core",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "core",
                table: "units");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "core",
                table: "units");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "core",
                table: "units");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "core",
                table: "cost_centers");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "core",
                table: "cost_centers");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "core",
                table: "cost_centers");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "core",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "core",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "core",
                table: "companies");
        }
    }
}
