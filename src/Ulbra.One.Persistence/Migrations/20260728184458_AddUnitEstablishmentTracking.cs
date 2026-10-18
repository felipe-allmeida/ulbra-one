using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitEstablishmentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cnpj",
                schema: "core",
                table: "units",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_matriz",
                schema: "core",
                table: "units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_units_cnpj",
                schema: "core",
                table: "units",
                column: "cnpj",
                unique: true,
                filter: "cnpj IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin_unit_id",
                schema: "finance",
                table: "payables",
                column: "origin_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_unit_id",
                schema: "accounting",
                table: "journal_lines",
                column: "unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_units_cnpj",
                schema: "core",
                table: "units");

            migrationBuilder.DropIndex(
                name: "ix_payables_origin_unit_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_unit_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "cnpj",
                schema: "core",
                table: "units");

            migrationBuilder.DropColumn(
                name: "is_matriz",
                schema: "core",
                table: "units");
        }
    }
}
