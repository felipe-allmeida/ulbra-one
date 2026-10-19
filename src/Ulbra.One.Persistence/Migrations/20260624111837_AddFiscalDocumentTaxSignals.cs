using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalDocumentTaxSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cfops",
                schema: "finance",
                table: "fiscal_documents",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "cofins_amount",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "icms_amount",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ipi_amount",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ncms",
                schema: "finance",
                table: "fiscal_documents",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "pis_amount",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_taxes_amount",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cfops",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "cofins_amount",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "icms_amount",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "ipi_amount",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "ncms",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "pis_amount",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "total_taxes_amount",
                schema: "finance",
                table: "fiscal_documents");
        }
    }
}
