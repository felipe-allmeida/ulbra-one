using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemittanceReturnAccountAndApplied : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "applied",
                schema: "finance",
                table: "remittance_returns",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paying_bank_account_id",
                schema: "finance",
                table: "remittance_returns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_remittance_returns_paying_bank_account_id_processed_at",
                schema: "finance",
                table: "remittance_returns",
                columns: new[] { "paying_bank_account_id", "processed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_remittance_returns_paying_bank_account_id_processed_at",
                schema: "finance",
                table: "remittance_returns");

            migrationBuilder.DropColumn(
                name: "applied",
                schema: "finance",
                table: "remittance_returns");

            migrationBuilder.DropColumn(
                name: "paying_bank_account_id",
                schema: "finance",
                table: "remittance_returns");
        }
    }
}
