using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuperintendenceRoleAndRemapRequisitionPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:approve", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:override-budget", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:write", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:triage", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role",
                columns: new[] { "id", "name" },
                values: new object[] { new Guid("66666666-6666-6666-6666-666666666666"), "Superintendence" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "requisitions:triage", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "core:read", new Guid("66666666-6666-6666-6666-666666666666") },
                    { "projects:read", new Guid("66666666-6666-6666-6666-666666666666") },
                    { "registry:read", new Guid("66666666-6666-6666-6666-666666666666") },
                    { "requisitions:approve", new Guid("66666666-6666-6666-6666-666666666666") },
                    { "requisitions:read", new Guid("66666666-6666-6666-6666-666666666666") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:triage", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "core:read", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "projects:read", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "registry:read", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:approve", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:read", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"));

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "requisitions:approve", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:override-budget", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:triage", new Guid("44444444-4444-4444-4444-444444444444") }
                });
        }
    }
}
