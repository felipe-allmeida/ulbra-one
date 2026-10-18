using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <summary>
    /// One-shot DATA migration (no schema change) — wipes the DEV database so the Development startup
    /// seed (<c>DevDataSeeder</c>) repopulates it from scratch. It empties every business table so the
    /// seed's <c>Companies.Count == 0</c> gate (and the per-section guards) re-run and rebuild the sample
    /// data, chart of accounts, categories, withholding rules, dev users and scope grants.
    ///
    /// <para><b>Environment-safe.</b> <c>MigrateAsync()</c> runs in every environment (not just Development)
    /// and DEV/PROD share the same database name (<c>ulbra_one</c>), so the wipe is guarded: it only runs
    /// when the DEV sentinel users (<c>%@ulbra.local</c>, seeded exclusively by <c>SeedLocalUsersAsync</c>
    /// in Development) are present. In PROD — and in the integration-test harness — that sentinel never
    /// exists, so this migration is a no-op there.</para>
    ///
    /// <para><b>Preserved:</b> the EF migrations history, the authorization reference data seeded by
    /// migrations (<c>identity.role</c>, <c>identity.role_claim</c>) — which the seed does NOT recreate —
    /// and the MassTransit/audit schemas.</para>
    ///
    /// <para><b>The destructive body was removed after it ran on DEV</b> (see PR that neutralized it).
    /// The file is kept in the chain (with its Designer) so migration ids stay contiguous — EF records
    /// applications by id in <c>__EFMigrationsHistory</c>, so DEV never re-runs it regardless of the body,
    /// and any database that never applied it now gets a harmless no-op instead of a data wipe. Mirrors
    /// <c>20260709162416_WipeLedgerAccountsForAelbraReseed</c>.</para>
    /// </summary>
    public partial class WipeDevDataForReseed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Body intentionally emptied — this one-shot DEV wipe already ran on DEV (see class summary).
            // The original guarded PL/pgSQL truncated all business tables (except the migrations history and
            // the migration-seeded identity.role/role_claim) so the Development seed could repopulate DEV.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the wipe is irreversible; there is nothing to roll back.
        }
    }
}
