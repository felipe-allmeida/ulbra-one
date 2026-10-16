using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ulbra.One.FirestoreImport;
using Ulbra.One.FirestoreImport.Storage;

// Local runner for the importer — invokes ImportRunner directly, no Azure Functions host / Azurite.
// Usage: dotnet run -- [preflight] [vendors] [users] [budgets] [bank-accounts]
//                      [requisitions-paid] [requisitions-in-payment] [requisitions-in-approval] [requisitions-cancelled]
//                      [requisitions] [payables] [payable-files] [projects] [adjustments] [project-links]
//                      [dead-letters] [all] [--apply] [--full]
// Default (no item) is a preflight. `all` = the 3 Fase-1 items; `requisitions` = the 4 stages (inclui canceladas). Item runs are
// a DRY-RUN unless --apply is passed. --full forces a complete Firestore read of `payables` (ignores the
// incremental cursor; default is incremental after the first run). `project-links` backfills the requisitions'
// project_id — run it after `requisitions` and `projects`.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFirestoreImport(builder.Configuration);
using var host = builder.Build();

var runner = host.Services.GetRequiredService<ImportRunner>();
var ct = CancellationToken.None;
var opts = new JsonSerializerOptions { WriteIndented = true };

var tokens = args.Where(a => !a.StartsWith('-')).Select(a => a.ToLowerInvariant()).ToHashSet();
var dryRun = !args.Contains("--apply");
var full = args.Contains("--full");
bool Has(params string[] names) => names.Any(tokens.Contains);

var all = Has("all");
var reqs = Has("requisitions");
var selection = new ImportSelection(
    Vendors: all || Has("vendors"),
    Users: all || Has("users"),
    Budgets: all || Has("budgets"),
    BankAccounts: Has("bank-accounts"),
    RequisitionsPaid: reqs || Has("requisitions-paid"),
    RequisitionsInPayment: reqs || Has("requisitions-in-payment"),
    RequisitionsInApproval: reqs || Has("requisitions-in-approval"),
    RequisitionsCancelled: reqs || Has("requisitions-cancelled"),
    Payables: Has("payables"),
    PayableFiles: Has("payable-files"),
    Projects: Has("projects"),
    Adjustments: Has("adjustments"),
    ProjectLinks: Has("project-links"));

var anyItem = selection != new ImportSelection();

// Exports the dead-letters (failures + warnings) to a CSV in the current dir + prints the per-entity counts.
if (Has("dead-letters"))
{
    var rows = await runner.GetDeadLettersAsync(null, ct);
    var path = Path.Combine(Directory.GetCurrentDirectory(), "dead-letters.csv");
    await File.WriteAllTextAsync(path, DeadLetterCsv.Build(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    Console.WriteLine($"# dead-letters: {rows.Count} linhas → {path}");
    foreach (var c in await runner.GetDeadLetterCountsAsync(ct))
        Console.WriteLine($"  {c.EntityType}: {c.Count}");
}

// Preflight when explicitly asked, or when nothing actionable was selected (the safe default).
if (Has("preflight") || (!anyItem && !Has("dead-letters")))
{
    Console.WriteLine("# preflight");
    Console.WriteLine(JsonSerializer.Serialize(await runner.PreflightAsync(ct), opts));
}

if (anyItem)
{
    Console.WriteLine($"# import (dryRun={dryRun}, full={full})");
    Console.WriteLine(JsonSerializer.Serialize(await runner.ImportAsync(selection, dryRun, full, ct), opts));
}
