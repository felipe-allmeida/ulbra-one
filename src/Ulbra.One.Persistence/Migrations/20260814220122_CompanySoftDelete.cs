using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanySoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_companies_cnpj",
                schema: "core",
                table: "companies");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "core",
                table: "companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_cnpj",
                schema: "core",
                table: "companies",
                column: "cnpj",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_companies_cnpj",
                schema: "core",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "core",
                table: "companies");

            migrationBuilder.CreateIndex(
                name: "ix_companies_cnpj",
                schema: "core",
                table: "companies",
                column: "cnpj",
                unique: true);
        }
    }
}
