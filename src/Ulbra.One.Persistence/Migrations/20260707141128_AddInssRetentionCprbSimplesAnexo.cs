using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInssRetentionCprbSimplesAnexo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cprb",
                schema: "fiscal",
                table: "withholding_rules",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simples_anexo",
                schema: "fiscal",
                table: "withholding_rules",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "cprb_foreseen",
                schema: "registry",
                table: "vendors",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simples_anexo",
                schema: "registry",
                table: "vendors",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "cprb_on_invoice",
                schema: "finance",
                table: "payables",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cprb",
                schema: "fiscal",
                table: "withholding_rules");

            migrationBuilder.DropColumn(
                name: "simples_anexo",
                schema: "fiscal",
                table: "withholding_rules");

            migrationBuilder.DropColumn(
                name: "cprb_foreseen",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "simples_anexo",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "cprb_on_invoice",
                schema: "finance",
                table: "payables");
        }
    }
}
