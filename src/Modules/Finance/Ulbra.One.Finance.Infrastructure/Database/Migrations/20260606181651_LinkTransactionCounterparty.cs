using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class LinkTransactionCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "counterparty",
                schema: "finance",
                table: "transactions",
                newName: "counterparty_name");

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "finance",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "finance",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_customer_id",
                schema: "finance",
                table: "transactions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_vendor_id",
                schema: "finance",
                table: "transactions",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_customer_id",
                schema: "finance",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_vendor_id",
                schema: "finance",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "finance",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "finance",
                table: "transactions");

            migrationBuilder.RenameColumn(
                name: "counterparty_name",
                schema: "finance",
                table: "transactions",
                newName: "counterparty");
        }
    }
}
