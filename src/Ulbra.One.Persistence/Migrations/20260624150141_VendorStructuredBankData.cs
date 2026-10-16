using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendorStructuredBankData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_info",
                schema: "registry",
                table: "vendors");

            migrationBuilder.AddColumn<string>(
                name: "bank_info_account",
                schema: "registry",
                table: "vendors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "bank_info_account_digit",
                schema: "registry",
                table: "vendors",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "bank_info_bank_code",
                schema: "registry",
                table: "vendors",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "bank_info_branch",
                schema: "registry",
                table: "vendors",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "bank_info_pix_key",
                schema: "registry",
                table: "vendors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_regime",
                schema: "registry",
                table: "vendors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Backfill existing rows with the enum's default member name; an empty
                // string is not a valid TaxRegime value and would throw on read.
                defaultValue: "Unknown");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_info_account",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_info_account_digit",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_info_bank_code",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_info_branch",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_info_pix_key",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "tax_regime",
                schema: "registry",
                table: "vendors");

            migrationBuilder.AddColumn<string>(
                name: "bank_info",
                schema: "registry",
                table: "vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
