using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFinancialChartAxis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purge the removed Financial chart-of-accounts axis. ChartType.Financial (enum value 1)
            // no longer exists; its rows become unreferenced once the payable columns below are dropped.
            migrationBuilder.Sql("DELETE FROM finance.chart_accounts WHERE chart_type = 1;");

            migrationBuilder.DropColumn(
                name: "financial_account_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "suggested_financial_account_id",
                schema: "finance",
                table: "payables");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "financial_account_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suggested_financial_account_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);
        }
    }
}
