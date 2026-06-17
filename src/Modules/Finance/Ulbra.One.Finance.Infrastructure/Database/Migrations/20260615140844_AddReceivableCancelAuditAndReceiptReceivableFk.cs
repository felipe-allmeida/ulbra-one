using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivableCancelAuditAndReceiptReceivableFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "finance",
                table: "receivables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_id",
                schema: "finance",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_receivables_receivable_id",
                schema: "finance",
                table: "receipts",
                column: "receivable_id",
                principalSchema: "finance",
                principalTable: "receivables",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_receipts_receivables_receivable_id",
                schema: "finance",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "cancelled_by_id",
                schema: "finance",
                table: "receivables");
        }
    }
}
