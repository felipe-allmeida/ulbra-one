using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.FirestoreImport;
using Xunit;
using Xunit.Abstractions;

namespace Ulbra.One.FirestoreImport.Tests;

/// <summary>Live smoke test — runs the real preflight/dry-run against a reachable Postgres + Firestore.
/// Gated by env var RUN_LIVE=1 so it never runs in CI. It exists mainly to execute the importer past the
/// local WDAC block (which blocks running the CLI/host exe directly but allows the test host).
/// Run: RUN_LIVE=1 dotnet test --filter LiveSmokeTests --logger "console;verbosity=detailed"</summary>
public class LiveSmokeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RUN_LIVE") == "1";

    private static IServiceProvider BuildServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] =
                    Environment.GetEnvironmentVariable("IMPORT_DB")
                    ?? "Host=localhost;Port=5433;Database=ulbra_one;Username=postgres;Password=postgres",
                ["Firestore:ProjectId"] = "spherical-cell-g07pf",
                ["Firestore:DatabaseId"] = "ai-studio-9c0ba35e-15c2-4fa9-96ad-eb738fcf5bb5",
            })
            .Build();

        return new ServiceCollection().AddFirestoreImport(config).BuildServiceProvider();
    }

    private void Dump(object value) =>
        output.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    [Fact]
    public async Task Preflight()
    {
        if (!Enabled) return;
        var runner = BuildServices().GetRequiredService<ImportRunner>();
        var report = await runner.PreflightAsync(CancellationToken.None);
        Dump(report);
        Assert.True(report.BudgetsTotal > 0);
    }

    [Fact]
    public async Task DryRunAll()
    {
        if (!Enabled) return;
        var runner = BuildServices().GetRequiredService<ImportRunner>();
        var selection = new ImportSelection(Vendors: true, Users: true, Budgets: true);
        Dump(await runner.ImportAsync(selection, dryRun: true, full: true, CancellationToken.None));
    }

    // Real write — only against a throwaway DB. Gated by RUN_APPLY=1 (separate from RUN_LIVE).
    [Fact]
    public async Task ApplyAll()
    {
        if (Environment.GetEnvironmentVariable("RUN_APPLY") != "1") return;
        var runner = BuildServices().GetRequiredService<ImportRunner>();
        var selection = new ImportSelection(
            Vendors: true, Users: true, Budgets: true, BankAccounts: true,
            RequisitionsPaid: true, RequisitionsInPayment: true, RequisitionsInApproval: true,
            RequisitionsCancelled: true,
            Payables: true, Projects: true, Adjustments: true, ProjectLinks: true);
        Dump(await runner.ImportAsync(selection, dryRun: false, full: true, CancellationToken.None));
    }
}
