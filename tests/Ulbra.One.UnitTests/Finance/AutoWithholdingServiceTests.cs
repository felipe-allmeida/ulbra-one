using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Unit tests for <see cref="AutoWithholdingService"/> — real Payable aggregates, a recording
/// IUnitOfWork fake and a stub calculator. Covers the Product-clear branch (AEL-696) and locks
/// the pre-existing Service/guard behavior.
/// </summary>
public class AutoWithholdingServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static Payable NewPayable(params Installment[] installments) =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Energia",
            costCenterId: Guid.NewGuid(), categoryId: null, installments);

    private static Payable TwoInstallmentPayable() => NewPayable(
        new Installment(1, new DateOnly(2026, 7, 10), 600m),
        new Installment(2, new DateOnly(2026, 8, 10), 400m));

    /// <summary>Serves entities via the async-compatible queryable and records every write call.</summary>
    private sealed class RecordingUow(params object[] entities) : IUnitOfWork
    {
        public List<object> Removed { get; } = [];
        public List<object> Added { get; } = [];
        public int SaveCalls { get; private set; }

        public IQueryable<T> ReadSet<T>() where T : class => new TestAsyncEnumerable<T>(entities.OfType<T>());
        public IQueryable<T> Set<T>() where T : class => new TestAsyncEnumerable<T>(entities.OfType<T>());
        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class
        {
            Added.Add(entity);
            return Task.CompletedTask;
        }
        public void Remove<T>(T entity) where T : class => Removed.Add(entity);
        public void RemoveRange<T>(IEnumerable<T> items) where T : class => Removed.AddRange(items);
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
        public void ClearChangeTracker() { }
    }

    private sealed class StubCalculator(params WithholdingResult[] results) : IWithholdingCalculator
    {
        public bool Called { get; private set; }

        public Task<IReadOnlyList<WithholdingResult>> CalculateAsync(WithholdingContext context, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult<IReadOnlyList<WithholdingResult>>(results);
        }
    }

    [Fact]
    public async Task Product_with_withholdings_clears_rows_and_restores_installment_nets()
    {
        var p = TwoInstallmentPayable();
        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]);
        p.SetType(PayableType.Product);
        var staleRow = p.Withholdings.Single();

        var uow = new RecordingUow();
        var calc = new StubCalculator();
        await new AutoWithholdingService(uow, calc).RecalculateAndApplyAsync(p, CancellationToken.None);

        Assert.Empty(p.Withholdings);
        Assert.Equal(1000m, p.NetTotal);
        Assert.All(p.Installments, i => Assert.Equal(i.Amount, i.NetAmount));
        Assert.Equal([staleRow], uow.Removed);
        Assert.Empty(uow.Added);
        Assert.False(calc.Called);
        Assert.Equal(0, uow.SaveCalls); // caller's transaction commits
    }

    [Fact]
    public async Task Product_without_withholdings_is_pure_noop()
    {
        var p = TwoInstallmentPayable();
        p.SetType(PayableType.Product);

        var uow = new RecordingUow();
        var calc = new StubCalculator();
        await new AutoWithholdingService(uow, calc).RecalculateAndApplyAsync(p, CancellationToken.None);

        Assert.Empty(uow.Removed);
        Assert.Empty(uow.Added);
        Assert.False(calc.Called);
    }

    [Fact]
    public async Task Null_type_leaves_existing_withholdings_untouched()
    {
        var p = TwoInstallmentPayable();
        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]);

        var uow = new RecordingUow();
        var calc = new StubCalculator();
        await new AutoWithholdingService(uow, calc).RecalculateAndApplyAsync(p, CancellationToken.None);

        var w = Assert.Single(p.Withholdings);
        Assert.Equal(150m, w.Amount);
        Assert.Empty(uow.Removed);
        Assert.Empty(uow.Added);
        Assert.False(calc.Called);
    }

    [Fact]
    public async Task Scheduled_product_payable_is_untouched()
    {
        // The Scheduled guard must fire BEFORE the Product-clear: the accounting incurral already
        // booked these withholdings, so a later type flip cannot wipe them.
        var p = TwoInstallmentPayable();
        p.SetType(PayableType.Product);
        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]);
        p.Approve("test-user", At);
        p.CompleteAccounting("test-user", At, hasFiscalDocument: true);

        var uow = new RecordingUow();
        var calc = new StubCalculator();
        await new AutoWithholdingService(uow, calc).RecalculateAndApplyAsync(p, CancellationToken.None);

        var w = Assert.Single(p.Withholdings);
        Assert.Equal(150m, w.Amount);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Empty(uow.Removed);
        Assert.False(calc.Called);
    }

    [Fact]
    public async Task Service_applies_calculator_results_and_replaces_rows()
    {
        var p = NewPayable(new Installment(1, new DateOnly(2026, 7, 10), 1000m));
        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, null, 100m)]);
        p.SetType(PayableType.Service);
        var staleRow = p.Withholdings.Single();

        // Empty uow: no FiscalDocument and no vendor row → regime Unknown, doc-less service line.
        var uow = new RecordingUow();
        var calc = new StubCalculator(new WithholdingResult(WithholdingTaxType.Irrf, 1000m, 0.015m, 15m));
        await new AutoWithholdingService(uow, calc).RecalculateAndApplyAsync(p, CancellationToken.None);

        Assert.True(calc.Called);
        var w = Assert.Single(p.Withholdings);
        Assert.Equal(15m, w.Amount);
        Assert.Equal(985m, p.NetTotal);
        Assert.Equal(985m, p.Installments.Single().NetAmount);
        Assert.Equal([staleRow], uow.Removed);
        Assert.Equal([w], uow.Added);
        Assert.Equal(0, uow.SaveCalls);
    }
}
