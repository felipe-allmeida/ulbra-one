using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountDreLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dre_line",
                schema: "accounting",
                table: "ledger_accounts",
                type: "integer",
                nullable: true);

            // Backfill the DRE line for already-seeded charts by code prefix. Mirrors
            // Ulbra.One.Accounting.Application.Seeding.DreLineClassifier (keep the two in sync).
            // Enum order: 0 GrossRevenue, 1 RevenueDeductions, 2 CostOfSales, 3 OtherOperatingIncome,
            // 4 OperatingExpenses, 5 FinancialRevenue, 6 FinancialExpense, 7 Other, 8 IncomeTax, 9 SocialContribution.
            migrationBuilder.Sql("""
                UPDATE accounting.ledger_accounts SET dre_line = CASE
                    WHEN code = '3.2.05' OR code LIKE '3.2.05.%' THEN 0
                    WHEN code = '3.2.10' OR code LIKE '3.2.10.%' THEN 1
                    WHEN code = '3.2.25' OR code LIKE '3.2.25.%' THEN 5
                    WHEN code = '3.2.20' OR code LIKE '3.2.20.%' OR code = '3.2.80' OR code LIKE '3.2.80.%' THEN 7
                    WHEN code = '3' OR code LIKE '3.%' THEN 3
                    WHEN code = '5' OR code LIKE '5.%' THEN 2
                    WHEN code = '4.2.30' OR code LIKE '4.2.30.%' THEN 6
                    WHEN code IN ('4.2.35.05.020', '4.2.35.05.060') THEN 9
                    WHEN code = '4.2.35' OR code LIKE '4.2.35.%' THEN 8
                    WHEN code = '4.2.25' OR code LIKE '4.2.25.%' THEN 7
                    WHEN code = '4' OR code LIKE '4.%' THEN 4
                    ELSE NULL
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dre_line",
                schema: "accounting",
                table: "ledger_accounts");
        }
    }
}
