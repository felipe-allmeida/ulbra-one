using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Infrastructure.Jobs;

/// <summary>Daily sweep: generates the day's CNAB remittance(s) for eligible installments (ready to download).
/// Never settles installments to Paid — that is a human "Dar baixa" or a .RET return import.</summary>
public sealed class RemittanceSweepJob(
    IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<RemittanceSweepJob> log, bool enabled, TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!enabled) return;
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
                var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                var r = await sweep.SweepAsync(asOf, ct);
                log.LogInformation("Remittance sweep {AsOf}: {Companies} companies, {Remittances} remittances, {Lines} lines",
                    asOf, r.CompaniesProcessed, r.RemittancesGenerated, r.InstallmentsIncluded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Remittance sweep failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
