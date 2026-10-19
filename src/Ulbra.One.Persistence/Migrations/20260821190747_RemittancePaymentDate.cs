using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemittancePaymentDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "payment_date",
                schema: "finance",
                table: "remittances",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Files generated before the column existed were always stamped with their generation date
            // (the service had no other option), so the backfill is exact — not a guess.
            migrationBuilder.Sql(
                "UPDATE finance.remittances SET payment_date = (generated_at AT TIME ZONE 'UTC')::date "
                + "WHERE payment_date = DATE '0001-01-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_date",
                schema: "finance",
                table: "remittances");
        }
    }
}
