using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Core.Domain.Entities;
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

        // Org structure — bootstrap only. SeedOrgStructureAsync is create-or-load, and the load half
        // exists to hand references back to the Dev/Stage flows that build on them. Production
        // discards the return value, so on a database that already has companies that half is pure
        // work — and it throws when the org there is not the one the seed hardcodes, which is the
        // case in production today. Guard on the same condition it checks internally.
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Set<Company>().AnyAsync(ct))
            await DevDataSeeder.SeedOrgStructureAsync(db, ct);

        // Budget is intentionally NOT seeded — import the real budget before enabling requisition approval.
        // Admins are NOT seeded — granted on Google login via Authentication:Google:BootstrapAdmins.
    }
}
