using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Api;

/// <summary>
/// Runs a single background job to completion and returns a process exit code — the shape an
/// Azure Container Apps <b>scheduled Job</b> expects (the process runs once and exits). Invoked via
/// <c>--job &lt;name&gt;</c>, it reuses the API image and DI graph but never starts the web pipeline
/// or the in-process hosted services, so a scheduled run and the web replicas don't double-execute.
/// Keep the known-job list in sync with the ACA Job definitions in Terraform.
/// </summary>
public static class JobRunner
{
    public const string RemittanceSweep = "remittance-sweep";

    public static async Task<int> RunAsync(
        IServiceProvider services, string jobName, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        switch (jobName)
        {
            case RemittanceSweep:
                return await RunRemittanceSweepAsync(services, clock, logger, ct);
            default:
                logger.LogError("Unknown job '{JobName}'. Known jobs: {Known}.", jobName, RemittanceSweep);
                return 2;
        }
    }

    private static async Task<int> RunRemittanceSweepAsync(
        IServiceProvider services, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
            var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

            var result = await sweep.SweepAsync(asOf, ct);

            logger.LogInformation(
                "RemittanceSweep job finished for {AsOf}: {Companies} companies processed, "
                + "{Remittances} remittances generated, {Installments} installments included.",
                asOf, result.CompaniesProcessed, result.RemittancesGenerated, result.InstallmentsIncluded);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemittanceSweep job failed.");
            return 1;
        }
    }
}
