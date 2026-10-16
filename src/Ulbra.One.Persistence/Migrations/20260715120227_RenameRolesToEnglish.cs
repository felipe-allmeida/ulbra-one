using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRolesToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "name",
                value: "Finance");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "name",
                value: "Controllership");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "name",
                value: "Accounting");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "name",
                value: "Financeiro");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "name",
                value: "Controladoria");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "name",
                value: "Contabil");
        }
    }
}
