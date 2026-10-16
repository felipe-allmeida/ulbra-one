using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>Domain rules of the blocking análise tributária stage (Serviço/Conta Fixa): Approve routes a
/// RequiresTaxAnalysis title to PendingTaxAnalysis, CompleteTaxAnalysis releases it to Scheduled (NF-gated),
/// retenções stay editable in the stage, and RecomputeSettlement must never promote a parked title.</summary>
public class PayableTaxAnalysisTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static Payable NewPayable(bool requiresTaxAnalysis, decimal total = 1000m) =>
        BuildPayable(requiresTaxAnalysis, total);

    private static Payable BuildPayable(bool requiresTaxAnalysis, decimal total)
    {
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Purchase, Guid.NewGuid(), "Fornecedor SA",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 6, 1), description: "Serviço",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(total, 1, new DateOnly(2026, 7, 10), 1));
        p.SetType(PayableType.Service);
        if (requiresTaxAnalysis) p.RequireTaxAnalysis();
        return p;
    }

    [Fact]
    public void Approve_routes_a_tax_title_to_PendingTaxAnalysis()
    {
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At);
        Assert.Equal(PayableStatus.PendingTaxAnalysis, p.Status);
    }

    [Fact]
    public void Approve_routes_a_non_tax_title_to_PendingAccounting()
    {
        var p = NewPayable(requiresTaxAnalysis: false);
        p.Approve("aprovador", At);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void CompleteTaxAnalysis_releases_to_Scheduled_with_fiscal_document()
    {
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At);
        p.CompleteTaxAnalysis("tributario", At, hasFiscalDocument: true);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Equal("tributario", p.TaxReviewedBy);
        Assert.Equal(At, p.TaxReviewedAt);
    }

    [Fact]
    public void CompleteTaxAnalysis_throws_without_fiscal_document()
    {
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At);
        Assert.Throws<InvalidOperationException>(() => p.CompleteTaxAnalysis("tributario", At, hasFiscalDocument: false));
        Assert.Equal(PayableStatus.PendingTaxAnalysis, p.Status);
    }

    [Fact]
    public void CompleteTaxAnalysis_throws_when_not_pending_tax_analysis()
    {
        // A non-tax title never enters PendingTaxAnalysis, so releasing it there is invalid.
        var p = NewPayable(requiresTaxAnalysis: false);
        p.Approve("aprovador", At); // → PendingAccounting
        Assert.Throws<InvalidOperationException>(() => p.CompleteTaxAnalysis("tributario", At, hasFiscalDocument: true));
    }

    [Fact]
    public void Withholdings_stay_editable_in_PendingTaxAnalysis()
    {
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At); // → PendingTaxAnalysis
        p.SetWithholdings([(WithholdingTaxType.Irrf, BaseAmount: 1000m, Rate: 0.015m, Amount: 15m)]);
        Assert.Equal(15m, p.TotalWithheld);
    }

    [Fact]
    public void Withholdings_lock_after_release_to_Scheduled()
    {
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At);
        p.CompleteTaxAnalysis("tributario", At, hasFiscalDocument: true); // → Scheduled
        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, BaseAmount: 1000m, Rate: 0.015m, Amount: 15m)]));
    }

    [Fact]
    public void RecomputeSettlement_does_not_promote_a_parked_tax_title()
    {
        // Guard: a PendingTaxAnalysis title has no payments; RecomputeSettlement must not promote it to Scheduled
        // (which would skip the NF gate + the accounting incurral event).
        var p = NewPayable(requiresTaxAnalysis: true);
        p.Approve("aprovador", At); // → PendingTaxAnalysis
        p.RecomputeSettlement();
        Assert.Equal(PayableStatus.PendingTaxAnalysis, p.Status);
    }
}
