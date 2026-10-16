using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPrimaryCnaeAndGlobalUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendors_company_id_document",
                schema: "registry",
                table: "vendors");

            migrationBuilder.AddColumn<string>(
                name: "primary_cnae_code",
                schema: "registry",
                table: "vendors",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_cnae_description",
                schema: "registry",
                table: "vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Vendors become globally unique by document. Resolve pre-existing cross-company duplicates:
            // remap every reference (requisitions, payables, transactions) from a duplicate to the
            // surviving row (earliest created_at, tie-break smallest id) BEFORE soft-deleting the
            // duplicates, so nothing is orphaned. The remap is NOT reversible (see Down).
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE vendor_dupe_map ON COMMIT DROP AS
                WITH survivors AS (
                    SELECT DISTINCT ON (document) id AS survivor_id, document
                    FROM registry.vendors
                    WHERE deleted_at IS NULL
                    ORDER BY document, created_at, id
                )
                SELECT v.id AS dup_id, s.survivor_id
                FROM registry.vendors v
                JOIN survivors s ON s.document = v.document
                WHERE v.deleted_at IS NULL AND v.id <> s.survivor_id;

                UPDATE budgeting.requisitions r SET vendor_id = m.survivor_id
                    FROM vendor_dupe_map m WHERE r.vendor_id = m.dup_id;
                UPDATE budgeting.requisitions r SET triage_approved_vendor_id = m.survivor_id
                    FROM vendor_dupe_map m WHERE r.triage_approved_vendor_id = m.dup_id;
                UPDATE finance.payables p SET vendor_id = m.survivor_id
                    FROM vendor_dupe_map m WHERE p.vendor_id = m.dup_id;
                UPDATE finance.transactions t SET vendor_id = m.survivor_id
                    FROM vendor_dupe_map m WHERE t.vendor_id = m.dup_id;

                UPDATE registry.vendors v SET deleted_at = now()
                    FROM vendor_dupe_map m WHERE v.id = m.dup_id;");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_document",
                schema: "registry",
                table: "vendors",
                column: "document",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverts the schema only — the duplicate soft-deletes and the reference remap from Up are not undone.
            migrationBuilder.DropIndex(
                name: "ix_vendors_document",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "primary_cnae_code",
                schema: "registry",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "primary_cnae_description",
                schema: "registry",
                table: "vendors");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_company_id_document",
                schema: "registry",
                table: "vendors",
                columns: new[] { "company_id", "document" },
                unique: true,
                filter: "deleted_at IS NULL");
        }
    }
}
