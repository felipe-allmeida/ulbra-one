using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequesterRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identity",
                table: "role",
                columns: new[] { "id", "name" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555555"), "Requester" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "core:read", new Guid("55555555-5555-5555-5555-555555555555") },
                    { "projects:read", new Guid("55555555-5555-5555-5555-555555555555") },
                    { "registry:read", new Guid("55555555-5555-5555-5555-555555555555") },
                    { "requisitions:read", new Guid("55555555-5555-5555-5555-555555555555") },
                    { "requisitions:write", new Guid("55555555-5555-5555-5555-555555555555") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "core:read", new Guid("55555555-5555-5555-5555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "projects:read", new Guid("55555555-5555-5555-5555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "registry:read", new Guid("55555555-5555-5555-5555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:read", new Guid("55555555-5555-5555-5555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:write", new Guid("55555555-5555-5555-5555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"));
        }
    }
}
