using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableLifecycleStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cast integer status to text (USING required by Postgres for int→varchar).
            migrationBuilder.Sql(
                "ALTER TABLE finance.payables ALTER COLUMN status TYPE character varying(30) USING status::text;");

            // Remap old int ordinals (0 PendingApproval,1 Approved,2 Rejected,3 Cancelled,4 Settled)
            // to the new string enum names (Approved → Scheduled, Settled → Paid).
            migrationBuilder.Sql(@"
                UPDATE finance.payables SET status = CASE status
                    WHEN '0' THEN 'PendingApproval'
                    WHEN '1' THEN 'Scheduled'
                    WHEN '2' THEN 'Rejected'
                    WHEN '3' THEN 'Cancelled'
                    WHEN '4' THEN 'Paid'
                    ELSE status END;");

            migrationBuilder.AddColumn<double>(
                name: "classification_confidence",
                schema: "finance",
                table: "payables",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "classification_rationale",
                schema: "finance",
                table: "payables",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suggested_accounting_account_id",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "classification_confidence",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "classification_rationale",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "suggested_accounting_account_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "suggested_financial_account_id",
                schema: "finance",
                table: "payables");

            // Reverse of Up: map status names back to the old int ordinals before the type change.
            // New-only states (Received, PendingClassification) have no old ordinal → fold to 0 (PendingApproval).
            migrationBuilder.Sql(
                "ALTER TABLE finance.payables ALTER COLUMN status TYPE integer USING (CASE status " +
                "WHEN 'PendingApproval' THEN 0 " +
                "WHEN 'Scheduled' THEN 1 " +
                "WHEN 'Rejected' THEN 2 " +
                "WHEN 'Cancelled' THEN 3 " +
                "WHEN 'Paid' THEN 4 " +
                "ELSE 0 END);");
        }
    }
}
