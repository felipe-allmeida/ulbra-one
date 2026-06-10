using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class PayableApprovalAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "finance",
                table: "payables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                schema: "finance",
                table: "payables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejected_at",
                schema: "finance",
                table: "payables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejected_by",
                schema: "finance",
                table: "payables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                schema: "finance",
                table: "payables");
        }
    }
}
