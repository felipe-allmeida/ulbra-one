using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoletoDigitableLineDuplicateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill before indexing: the linha digitável used to be stored as typed (mask separators and
            // all), and the duplicate check compares by equality. Rows written with a mask would never match
            // their own bare-digit twin, so the same boleto could still be paid twice. Canonicalize first,
            // then index — BoletoLine.Canonical now keeps every new write in this shape.
            foreach (var (schema, table) in new[]
            {
                ("finance", "installments"),
                ("budgeting", "requisition_installment"),
                ("budgeting", "requisition_cycle"),
            })
            {
                migrationBuilder.Sql($"""
                    UPDATE {schema}.{table}
                    SET boleto_digitable_line = NULLIF(regexp_replace(boleto_digitable_line, '[^0-9]', '', 'g'), '')
                    WHERE boleto_digitable_line IS NOT NULL
                      AND boleto_digitable_line IS DISTINCT FROM
                          NULLIF(regexp_replace(boleto_digitable_line, '[^0-9]', '', 'g'), '');
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "ix_requisition_installment_boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_installment",
                column: "boleto_digitable_line");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_cycle_boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_cycle",
                column: "boleto_digitable_line");

            migrationBuilder.CreateIndex(
                name: "ix_installments_boleto_digitable_line",
                schema: "finance",
                table: "installments",
                column: "boleto_digitable_line");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisition_installment_boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_installment");

            migrationBuilder.DropIndex(
                name: "ix_requisition_cycle_boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_cycle");

            migrationBuilder.DropIndex(
                name: "ix_installments_boleto_digitable_line",
                schema: "finance",
                table: "installments");
        }
    }
}
