using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Api;
using Ulbra.One.Finance.Application.Remittances;
using Xunit;

namespace Ulbra.One.IntegrationTests.Api;

/// <summary>
/// Lightweight tests for the run-once job entrypoint (--job). Builds a minimal DI container with a
/// stub sweep service — no web host, no database — and asserts the process exit-code contract that
/// the Azure Container Apps scheduled Job relies on.
/// </summary>
public class JobRunnerTests
{
    private sealed class StubSweep(Func<DateOnly, Task<RemittanceSweepResult>> run) : IRemittanceSweepService
    {
        public Task<RemittanceSweepResult> SweepAsync(DateOnly asOf, CancellationToken ct) => run(asOf);
    }

    private static IServiceProvider Provider(IRemittanceSweepService sweep)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => sweep);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RemittanceSweep_success_returns_exit_code_0()
    {
        var provider = Provider(new StubSweep(_ => Task.FromResult(new RemittanceSweepResult(1, 1, 2))));
        var code = await JobRunner.RunAsync(
            provider, JobRunner.RemittanceSweep, TimeProvider.System, NullLogger.Instance, CancellationToken.None);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task RemittanceSweep_failure_returns_exit_code_1()
    {
        var provider = Provider(new StubSweep(_ => throw new InvalidOperationException("boom")));
        var code = await JobRunner.RunAsync(
            provider, JobRunner.RemittanceSweep, TimeProvider.System, NullLogger.Instance, CancellationToken.None);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Unknown_job_returns_exit_code_2()
    {
        var provider = Provider(new StubSweep(_ => Task.FromResult(new RemittanceSweepResult(0, 0, 0))));
        var code = await JobRunner.RunAsync(
            provider, "does-not-exist", TimeProvider.System, NullLogger.Instance, CancellationToken.None);
        Assert.Equal(2, code);
    }
}
