using Ulbra.One.Persistence;

namespace Ulbra.One.Api;

/// <summary>
/// Seeds the PRODUCTION database with foundational config only — the AELBRA org (companies/units/cost
/// centers + coverage), ledger chart, withholding rules and expense categories. Deliberately does NOT seed:
/// budget (an absent CostCenterUnitBudget cell blocks requisition approval by design, so the real budget must
/// be imported before the workflow is enabled); users/admins (admins are granted Admin on Google login via
/// Authentication:Google:BootstrapAdmins, not pre-created here); nor any mock transactional data. Every step
/// is idempotent. Production-only; Program.cs invokes it under a Postgres advisory lock and logs-and-continues
/// on failure.
/// </summary>
public static class ProdDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        // Reference data (no deps; each idempotent via AnyAsync/Count).
        await DevDataSeeder.SeedLedgerChartAsync(services, ct);       // AELBRA chart (classification + posting)
        await DevDataSeeder.SeedWithholdingRulesAsync(services, ct);  // withholding rules
        await DevDataSeeder.SeedExpenseCategoriesAsync(services, ct); // expense categories

        // Org structure — create-or-load (AELBRA companies/units/cost centers + coverage).
        using var scope = services.CreateScope();
        await DevDataSeeder.SeedOrgStructureAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);

        // Budget is intentionally NOT seeded — import the real budget before enabling requisition approval.
        // Admins are NOT seeded — granted on Google login via Authentication:Google:BootstrapAdmins.
    }
}
