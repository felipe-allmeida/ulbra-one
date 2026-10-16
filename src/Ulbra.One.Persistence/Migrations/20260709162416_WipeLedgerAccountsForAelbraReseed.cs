using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <summary>
    /// One-shot DATA migration (no schema change) — <b>ALREADY EXECUTED</b>. Its original body wiped the
    /// pre-AELBRA chart of accounts and its dependents (journal entries/lines, pending postings, loose refs
    /// on payables/receivables) so the Development startup seed would repopulate the deployed DEV database
    /// with the AELBRA chart (1119 accounts, 546 active / 573 inactive) — AEL-442.
    ///
    /// The destructive body was removed after it ran on DEV (deploy of PR #54, 2026-07). The file is kept in
    /// the chain (with its Designer) so migration ids stay contiguous — EF records applications by id in
    /// <c>__EFMigrationsHistory</c>, so databases that already applied it never re-run it regardless of the
    /// body, and databases that never applied it now get a harmless no-op instead of a chart wipe.
    /// </summary>
    public partial class WipeLedgerAccountsForAelbraReseed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Body intentionally emptied — this one-shot wipe already ran on DEV (see class summary).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the original wipe was irreversible; nothing to roll back.
        }
    }
}
