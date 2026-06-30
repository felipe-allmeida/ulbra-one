using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromoteNfseServiceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "simples_declaration_provided",
                schema: "finance",
                table: "payables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "cnae",
                schema: "finance",
                table: "fiscal_documents",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "iss_retido_na_nota",
                schema: "finance",
                table: "fiscal_documents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lc116subitem",
                schema: "finance",
                table: "fiscal_documents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "municipal_service_code",
                schema: "finance",
                table: "fiscal_documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "nota_iss_rate",
                schema: "finance",
                table: "fiscal_documents",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_municipality_ibge",
                schema: "finance",
                table: "fiscal_documents",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "simples_declaration_provided",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "cnae",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "iss_retido_na_nota",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "lc116subitem",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "municipal_service_code",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "nota_iss_rate",
                schema: "finance",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "service_municipality_ibge",
                schema: "finance",
                table: "fiscal_documents");
        }
    }
}
