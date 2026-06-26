using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableTaxWithholding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                schema: "finance",
                table: "installments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Backfill: existing installment rows default to 0 but must reflect the gross amount
            // (no withholding existed before this feature). New rows have NetAmount set by the ctor.
            migrationBuilder.Sql("UPDATE finance.installments SET net_amount = amount;");

            migrationBuilder.CreateTable(
                name: "payable_withholdings",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_withholdings", x => x.id);
                    table.ForeignKey(
                        name: "fk_payable_withholdings_payables_payable_id",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payable_withholdings_payable_id",
                schema: "finance",
                table: "payable_withholdings",
                column: "payable_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payable_withholdings",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "net_amount",
                schema: "finance",
                table: "installments");
        }
    }
}
