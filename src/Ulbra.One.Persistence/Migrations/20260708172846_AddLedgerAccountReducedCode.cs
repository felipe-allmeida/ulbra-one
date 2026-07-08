using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountReducedCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reduced_code",
                schema: "accounting",
                table: "ledger_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_reduced_code",
                schema: "accounting",
                table: "ledger_accounts",
                column: "reduced_code",
                unique: true,
                filter: "reduced_code IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_accounts_reduced_code",
                schema: "accounting",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "reduced_code",
                schema: "accounting",
                table: "ledger_accounts");
        }
    }
}
