using Ulbra.One.Accounting.Application.Periods;
using Ulbra.One.Accounting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class AccountingPeriodGuardTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly DateOnly Competence = new(2026, 7, 15);

    [Fact]
    public async Task No_period_row_is_treated_as_open()
    {
        var uow = new AcctStubUow();
        Assert.False(await AccountingPeriodGuard.IsClosedAsync(uow, Company, Competence, CancellationToken.None));
        Assert.Null(await AccountingPeriodGuard.EnsureOpenAsync(uow, Company, Competence, CancellationToken.None));
    }

    [Fact]
    public async Task Closed_period_blocks()
    {
        var period = new AccountingPeriod(Company, 2026, 7);
        period.Close();
        var uow = new AcctStubUow(period);

        Assert.True(await AccountingPeriodGuard.IsClosedAsync(uow, Company, Competence, CancellationToken.None));
        Assert.NotNull(await AccountingPeriodGuard.EnsureOpenAsync(uow, Company, Competence, CancellationToken.None));
    }

    [Fact]
    public async Task Closing_period_still_accepts_postings()
    {
        var period = new AccountingPeriod(Company, 2026, 7);
        period.StartClosing();
        var uow = new AcctStubUow(period);

        Assert.False(await AccountingPeriodGuard.IsClosedAsync(uow, Company, Competence, CancellationToken.None));
    }

    [Fact]
    public async Task Closed_period_of_another_company_does_not_block()
    {
        var other = new AccountingPeriod(Guid.NewGuid(), 2026, 7);
        other.Close();
        var uow = new AcctStubUow(other);

        Assert.False(await AccountingPeriodGuard.IsClosedAsync(uow, Company, Competence, CancellationToken.None));
    }
}
