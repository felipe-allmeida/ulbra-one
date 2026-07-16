using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedAccountingPayablesWriteClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[] { "payables:write", new Guid("44444444-4444-4444-4444-444444444444") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "payables:write", new Guid("44444444-4444-4444-4444-444444444444") });
        }
    }
}
