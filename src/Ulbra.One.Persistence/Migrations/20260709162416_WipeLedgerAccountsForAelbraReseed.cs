using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <summary>
    /// One-shot DATA migration (no schema change): wipes the pre-AELBRA chart of accounts and its
    /// dependents so the startup seed — which only populates when <c>accounting.ledger_accounts</c>
    /// is empty — repopulates the deployed DEV database with the AELBRA chart (1119 accounts,
    /// 546 active / 573 inactive) on the next deploy. Dev data is disposable by decision (AEL-442).
    ///
    /// Safety:
    /// - Fresh databases (integration-test containers, brand-new environments) run this against
    ///   empty tables — a no-op.
    /// - Ordering honours the only real FK into ledger_accounts (journal_lines → Restrict):
    ///   journal entries are deleted first (lines cascade), then the loose Guid refs on
    ///   payables/receivables are nulled, then the chart itself is deleted.
    /// - Non-Development environments do NOT auto-reseed (the seeder is dev-only); recovery there
    ///   is POST /api/ledger-accounts/seed (idempotent). Today only the deployed DEV database has
    ///   chart data, which is exactly the intended target.
    /// </summary>
    public partial class WipeLedgerAccountsForAelbraReseed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Journal first: journal_lines → ledger_accounts is Restrict, but journal_entries →
            // journal_lines is Cascade, so deleting the entries releases the FK on the chart.
            migrationBuilder.Sql("DELETE FROM accounting.journal_entries;");

            // Pending postings reference chart codes as text — stale against the new chart.
            migrationBuilder.Sql("DELETE FROM accounting.pending_accounting_postings;");

            // Loose Guid refs (no FK constraints): unclassify payables and clear AI suggestions +
            // receivable revenue accounts so nothing dangles on deleted account ids.
            migrationBuilder.Sql(@"
                UPDATE finance.payables
                   SET accounting_account_id = NULL, suggested_accounting_account_id = NULL
                 WHERE accounting_account_id IS NOT NULL OR suggested_accounting_account_id IS NOT NULL;");
            migrationBuilder.Sql(@"
                UPDATE finance.receivables
                   SET ledger_account_id = NULL
                 WHERE ledger_account_id IS NOT NULL;");

            // Finally the chart itself (parent_id is a loose self-reference — full delete is safe).
            migrationBuilder.Sql("DELETE FROM accounting.ledger_accounts;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible one-shot data wipe: the deleted dev data cannot be restored. Rolling back
            // leaves the tables empty; the Development startup seed repopulates the chart.
        }
    }
}
