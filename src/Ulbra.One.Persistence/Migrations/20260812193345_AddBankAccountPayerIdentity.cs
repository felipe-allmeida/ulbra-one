using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountPayerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payer_cnpj",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(14)",
                maxLength: 14,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payer_legal_name",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payer_cnpj",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "payer_legal_name",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
