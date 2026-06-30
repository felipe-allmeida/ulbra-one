using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallmentScheduleAndVendorPaymentSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "payment_schedule_day_of_month",
                schema: "registry",
                table: "vendors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_schedule_mode",
                schema: "registry",
                table: "vendors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "OnDueDate");

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "finance",
                table: "installments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_held",
                schema: "finance",
                table: "installments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "scheduled_payment_date",
                schema: "finance",
                table: "installments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Backfill: existing rows get scheduled_payment_date = due_date (the original intent)
            // and is_held is already defaulted to false by the column default above.
            migrationBuilder.Sql(
                "UPDATE finance.installments SET scheduled_payment_date = due_date WHERE scheduled_payment_date = '0001-01-01';");

            // Belt-and-suspenders: any vendor row that somehow has a null or empty payment_schedule_mode
            // (e.g. from a partial earlier migration on a pre-existing DB) is reset to the safe default.
            migrationBuilder.Sql(
                "UPDATE registry.vendors SET payment_schedule_mode = 'OnDueDate' WHERE payment_schedule_mode IS NULL OR payment_schedule_mode = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_schedule_day_of_month",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "payment_schedule_mode",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "finance",
                table: "installments");

            migrationBuilder.DropColumn(
                name: "is_held",
                schema: "finance",
                table: "installments");

            migrationBuilder.DropColumn(
                name: "scheduled_payment_date",
                schema: "finance",
                table: "installments");
        }
    }
}
