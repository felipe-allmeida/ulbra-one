using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Xunit;

namespace Ulbra.One.FirestoreImport.Tests;

public class RequisitionMappingTests
{
    private static RequisitionDoc Doc(
        string status = "PAGO", string type = "Compra", string estimateType = "integral",
        string paymentSystem = "Único", decimal estimateValue = 1000m,
        string start = "2026-08", string? end = null, string? firstDue = "2026-08-10",
        long? count = null, string? freq = null)
        => new RequisitionDoc("REQ_1", "200", "ENSINO", status, type, estimateType, paymentSystem, estimateValue,
            start, end, "88.332.466/0001-04", "a@b.com", "desc", 1, firstDue, count, freq, null, null, null);

    [Theory]
    [InlineData("Finalizada", "Finalized")]
    [InlineData("PAGO", "Paid")]
    [InlineData("Em Pagamento", "InPayment")]
    [InlineData("AGUARDANDO DOCUMENTO FISCAL", "AwaitingFiscalDocument")]
    [InlineData("Cancelado", "Cancelled")]
    [InlineData("Triagem", "Triage")]
    [InlineData("qualquer", "Triage")]
    public void MapStatus_maps_legacy_labels(string legacy, string expected)
        => Assert.Equal(expected, RequisitionMapping.MapStatus(legacy));

    [Theory]
    [InlineData("Mensal", "Monthly")]
    [InlineData("Quinzenal", "Biweekly")]
    [InlineData("Semestral", "Semiannual")]
    [InlineData("Anual", "Annual")]
    [InlineData("", "Monthly")]
    [InlineData(null, "Monthly")]
    public void MapFrequency_maps_cadence(string? legacy, string expected)
        => Assert.Equal(expected, RequisitionMapping.MapFrequency(legacy));

    [Fact]
    public void Stages_partition_the_consuming_statuses()
    {
        Assert.Equal(["Finalized", "Paid"], RequisitionMapping.StagePaid.OrderBy(s => s));
        Assert.Contains("InPayment", RequisitionMapping.StageInPayment);
        Assert.Contains("AwaitingFiscalDocument", RequisitionMapping.StageInPayment);
        Assert.Contains("Triage", RequisitionMapping.StageApproval);
        // Cancelled is in no stage → never imported.
        Assert.DoesNotContain("Cancelled", RequisitionMapping.StagePaid);
        Assert.DoesNotContain("Cancelled", RequisitionMapping.StageInPayment);
        Assert.DoesNotContain("Cancelled", RequisitionMapping.StageApproval);
    }

    [Fact]
    public void IsRealized_only_paid_and_finalized()
    {
        Assert.True(RequisitionMapping.IsRealized("Paid"));
        Assert.True(RequisitionMapping.IsRealized("Finalized"));
        Assert.False(RequisitionMapping.IsRealized("InPayment"));
        Assert.False(RequisitionMapping.IsRealized("Triage"));
    }

    [Fact]
    public void Integral_unico_is_full_single_installment_on_start()
    {
        var p = RequisitionMapping.ResolvePlan(Doc(
            estimateType: "integral", paymentSystem: "Único", estimateValue: 15420.02m,
            start: "2026-08", firstDue: "2026-08-10"));

        Assert.Equal("Full", p.EstimateType);
        Assert.Equal(15420.02m, p.EstimateValue);
        Assert.Null(p.EndCompetency);
        Assert.Null(p.PaymentInstallmentsCount);
        Assert.Null(p.PaymentFrequency);

        var inst = Assert.Single(p.Installments);
        Assert.Equal(1, inst.Sequence);
        Assert.Equal(new DateOnly(2026, 8, 10), inst.DueDate);
        Assert.Equal(15420.02m, inst.Amount);

        Assert.Equal(("2026-08", 15420.02m), Assert.Single(p.Payments));
    }

    [Fact]
    public void Mensal_parcelado_splits_total_by_count_not_month_span()
    {
        var p = RequisitionMapping.ResolvePlan(Doc(
            estimateType: "mensal", paymentSystem: "Parcelado", estimateValue: 1000m,
            start: "2026-07", end: "2027-01", firstDue: "2026-07-20", count: 7, freq: "Mensal"));

        Assert.Equal("Monthly", p.EstimateType);
        Assert.Equal(7000m, p.EstimateValue);       // per-installment 1000 × 7 (the total)
        Assert.Equal("2026-07", p.StartCompetency);
        Assert.Equal("2027-01", p.EndCompetency);   // start + 6
        Assert.Equal(7, p.Installments.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), p.Installments[0].DueDate);
        Assert.Equal(new DateOnly(2027, 1, 20), p.Installments[6].DueDate);
        Assert.All(p.Installments, i => Assert.Equal(1000m, i.Amount));
        Assert.Equal(
            ["2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12", "2027-01"],
            p.Payments.Select(x => x.Competency));
        Assert.Equal(7, p.PaymentInstallmentsCount);
        Assert.Equal("Monthly", p.PaymentFrequency);
    }

    [Fact]
    public void Quinzenal_bills_two_per_month_and_total_is_not_halved()
    {
        // The old month-span heuristic gave 80000 × 1 month = 80000; the count-based total is 160000.
        var p = RequisitionMapping.ResolvePlan(Doc(
            estimateType: "mensal", paymentSystem: "Parcelado", estimateValue: 80000m,
            start: "2026-08", end: "2026-08", firstDue: "2026-08-22", count: 2, freq: "Quinzenal"));

        Assert.Equal(160000m, p.EstimateValue);
        Assert.Equal("2026-08", p.StartCompetency);
        Assert.Equal("2026-08", p.EndCompetency);   // both parcelas fold into August (floor(1/2)=0)
        Assert.Equal(2, p.Installments.Count);
        Assert.Equal(["2026-08", "2026-08"], p.Payments.Select(x => x.Competency));
        Assert.Equal(160000m, p.Payments.Sum(x => x.Amount));
        Assert.Equal("Biweekly", p.PaymentFrequency);
    }

    [Fact]
    public void Quinzenal_eight_parcelas_span_four_months_two_each()
    {
        var p = RequisitionMapping.ResolvePlan(Doc(
            estimateType: "mensal", paymentSystem: "Parcelado", estimateValue: 100m,
            start: "2026-05", end: "2026-08", firstDue: "2026-05-10", count: 8, freq: "Quinzenal"));

        Assert.Equal(800m, p.EstimateValue);
        Assert.Equal("2026-08", p.EndCompetency);   // start + floor(7/2) = +3 → August
        Assert.Equal(
            ["2026-05", "2026-05", "2026-06", "2026-06", "2026-07", "2026-07", "2026-08", "2026-08"],
            p.Payments.Select(x => x.Competency));
    }

    [Fact]
    public void Conta_fixa_recurring_is_single_open_ended_template()
    {
        var p = RequisitionMapping.ResolvePlan(Doc(
            type: "Conta Fixa", estimateType: "mensal", paymentSystem: "Recorrente", estimateValue: 2500m,
            start: "2026-08", end: "2100-12", firstDue: "2026-08-05", freq: "Mensal"));

        Assert.Equal("FixedExpense", p.Type);
        Assert.Equal("Monthly", p.EstimateType);
        Assert.Equal(2500m, p.EstimateValue);       // per-month value stays (not multiplied)
        Assert.Null(p.EndCompetency);               // open-ended
        var inst = Assert.Single(p.Installments);   // recurring → exactly one installment (domain rule)
        Assert.Equal(2500m, inst.Amount);
        Assert.Equal(("2026-08", 2500m), Assert.Single(p.Payments));
    }

    [Theory]
    [InlineData("integral", "Único", 1500, "2026-08", null, null, null)]
    [InlineData("mensal", "Parcelado", 300, "2026-07", "2026-12", 6L, "Mensal")]
    [InlineData("mensal", "Parcelado", 500, "2026-08", "2026-09", 4L, "Quinzenal")]
    [InlineData("mensal", "Recorrente", 900, "2026-08", "2100-01", null, "Mensal")]
    public void Installment_amounts_always_sum_to_estimate_value(
        string estimateType, string paymentSystem, int value, string start, string? end, long? count, string? freq)
    {
        var p = RequisitionMapping.ResolvePlan(Doc(
            estimateType: estimateType, paymentSystem: paymentSystem, estimateValue: value,
            start: start, end: end, firstDue: $"{start}-05", count: count, freq: freq));

        Assert.Equal(p.EstimateValue, p.Installments.Sum(i => i.Amount)); // One's domain invariant
    }
}
