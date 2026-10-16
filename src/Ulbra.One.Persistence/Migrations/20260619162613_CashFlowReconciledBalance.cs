using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowReconciledBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "reconciled_at",
                schema: "finance",
                table: "bank_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reconciled_balance",
                schema: "finance",
                table: "bank_accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reconciled_at",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "reconciled_balance",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
