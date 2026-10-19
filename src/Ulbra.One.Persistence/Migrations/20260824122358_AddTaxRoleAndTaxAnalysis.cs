using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxRoleAndTaxAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_tax_analysis",
                schema: "finance",
                table: "payables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tax_reviewed_at",
                schema: "finance",
                table: "payables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_reviewed_by",
                schema: "finance",
                table: "payables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role",
                columns: new[] { "id", "name" },
                values: new object[] { new Guid("88888888-8888-8888-8888-888888888888"), "Tax" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "payables:tax", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "core:read", new Guid("88888888-8888-8888-8888-888888888888") },
                    { "fiscal:read", new Guid("88888888-8888-8888-8888-888888888888") },
                    { "payables:read", new Guid("88888888-8888-8888-8888-888888888888") },
                    { "payables:tax", new Guid("88888888-8888-8888-8888-888888888888") },
                    { "registry:read", new Guid("88888888-8888-8888-8888-888888888888") },
                    { "reports:read", new Guid("88888888-8888-8888-8888-888888888888") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "payables:tax", new Guid("77777777-7777-7777-7777-777777777777") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "core:read", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "fiscal:read", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "payables:read", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "payables:tax", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "registry:read", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "reports:read", new Guid("88888888-8888-8888-8888-888888888888") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"));

            migrationBuilder.DropColumn(
                name: "requires_tax_analysis",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "tax_reviewed_at",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "tax_reviewed_by",
                schema: "finance",
                table: "payables");
        }
    }
}
