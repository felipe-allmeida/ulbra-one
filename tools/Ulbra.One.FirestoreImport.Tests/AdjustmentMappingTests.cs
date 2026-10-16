using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Xunit;

namespace Ulbra.One.FirestoreImport.Tests;

public class AdjustmentMappingTests
{
    private static readonly DateTimeOffset Created = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cancelled = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    private static AdjustmentDoc Doc(
        string type = "Suplementação", string? revisionAction = null, string status = "Aprovado",
        string? competency = "2026-08", string? sourceCompetency = null, decimal? value = 1000m,
        IReadOnlyDictionary<string, decimal>? competencyValues = null,
        string? justification = "ajuste", string? counterparts = null, string? requesterEmail = "a@b.com",
        long? seqNum = 1, DateTimeOffset? cancelledAt = null, string? cancelReason = null)
        => new("ADQ_1", "200", "ENSINO", type, revisionAction, status, competency, sourceCompetency, value,
            competencyValues ?? new Dictionary<string, decimal>(), justification, counterparts, requesterEmail,
            seqNum, Created, Updated, cancelledAt, cancelReason);

    [Theory]
    [InlineData("Aprovado", "Approved")]
    [InlineData("Cancelado", "Cancelled")]
    [InlineData("Aguardando_Administrador", "AwaitingApproval")]
    [InlineData("Complementação", "AwaitingTriage")]
    [InlineData("qualquer", "AwaitingTriage")]
    public void MapStatus_maps_legacy_labels(string legacy, string expected)
        => Assert.Equal(expected, AdjustmentMapping.MapStatus(legacy));

    [Theory]
    [InlineData("Suplementação", null, "Supplementation")]
    [InlineData("Redução", null, "Reduction")]
    [InlineData("Transferência", null, "Transfer")]
    [InlineData("Revisão Orçamentária", "Aumento", "Supplementation")]
    [InlineData("Revisão Orçamentária", "Diminuição", "Reduction")]
    public void MapType_maps_type_and_revision_direction(string type, string? action, string expected)
        => Assert.Equal(expected, AdjustmentMapping.MapType(type, action));

    [Fact]
    public void Supplementation_is_a_single_row_on_the_docs_competency()
    {
        var entries = AdjustmentMapping.ResolveEntries(Doc(type: "Suplementação", competency: "2026-08", value: 500m));

        var e = Assert.Single(entries);
        Assert.Equal("Supplementation", e.Type);
        Assert.Equal("2026-08", e.Competency);
        Assert.Equal(500m, e.Value);
        Assert.Null(e.SourceCompetency);
    }

    [Fact]
    public void Transfer_carries_the_source_competency()
    {
        var entries = AdjustmentMapping.ResolveEntries(
            Doc(type: "Transferência", competency: "2026-12", sourceCompetency: "2026-08", value: 300m));

        var e = Assert.Single(entries);
        Assert.Equal("Transfer", e.Type);
        Assert.Equal("2026-12", e.Competency);
        Assert.Equal("2026-08", e.SourceCompetency);
    }

    [Fact]
    public void Only_transfer_keeps_a_source_competency()
    {
        // A non-Transfer with a stray sourceCompetency must not leak it into the target row.
        var entries = AdjustmentMapping.ResolveEntries(
            Doc(type: "Suplementação", sourceCompetency: "2026-08"));
        Assert.Null(Assert.Single(entries).SourceCompetency);
    }

    [Fact]
    public void Revision_fans_out_one_row_per_competency_and_sums_to_the_total()
    {
        var values = new Dictionary<string, decimal> { ["2026-08"] = 100m, ["2026-09"] = 200m, ["2026-10"] = 300m };
        var entries = AdjustmentMapping.ResolveEntries(
            Doc(type: "Revisão Orçamentária", revisionAction: "Aumento", value: 600m, competencyValues: values));

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal("Supplementation", e.Type));
        Assert.All(entries, e => Assert.Null(e.SourceCompetency));
        Assert.Equal(600m, entries.Sum(e => e.Value)); // value == Σ competencyValues (source invariant)
        Assert.Equal(["2026-08", "2026-09", "2026-10"], entries.Select(e => e.Competency));
    }

    [Fact]
    public void Revision_direction_diminuicao_is_reduction()
    {
        var values = new Dictionary<string, decimal> { ["2026-08"] = 50m };
        var entries = AdjustmentMapping.ResolveEntries(
            Doc(type: "Revisão Orçamentária", revisionAction: "Diminuição", value: 50m, competencyValues: values));

        Assert.Equal("Reduction", Assert.Single(entries).Type);
    }

    [Fact]
    public void Revision_drops_invalid_competency_keys()
    {
        var values = new Dictionary<string, decimal> { ["2026-08"] = 100m, ["bad"] = 999m, ["2026-13"] = 5m };
        var entries = AdjustmentMapping.ResolveEntries(
            Doc(type: "Revisão Orçamentária", revisionAction: "Aumento", value: 100m, competencyValues: values));

        Assert.Equal(["2026-08"], entries.Select(e => e.Competency));
    }

    [Fact]
    public void Approved_builds_a_two_step_trail()
    {
        var t = AdjustmentMapping.BuildTransitions("Approved", Created, Updated, null, null);

        Assert.Equal(2, t.Count);
        Assert.Equal(("SendToApproval", "AwaitingTriage", "AwaitingApproval", (DateTimeOffset?)Created),
            (t[0].Action, t[0].FromStatus, t[0].ToStatus, t[0].OccurredAt));
        Assert.Equal(("Approve", "AwaitingApproval", "Approved", (DateTimeOffset?)Updated),
            (t[1].Action, t[1].FromStatus, t[1].ToStatus, t[1].OccurredAt));
    }

    [Fact]
    public void Cancelled_builds_a_single_cancel_transition_with_reason()
    {
        var t = AdjustmentMapping.BuildTransitions("Cancelled", Created, Updated, Cancelled, "sem saldo");

        var only = Assert.Single(t);
        Assert.Equal("Cancel", only.Action);
        Assert.Equal("AwaitingTriage", only.FromStatus);
        Assert.Equal("Cancelled", only.ToStatus);
        Assert.Equal(Cancelled, only.OccurredAt);
        Assert.Equal("sem saldo", only.Reason);
    }

    [Fact]
    public void Cancelled_without_cancelled_at_falls_back_to_updated()
    {
        var t = AdjustmentMapping.BuildTransitions("Cancelled", Created, Updated, null, null);
        Assert.Equal(Updated, Assert.Single(t).OccurredAt);
    }

    [Fact]
    public void AwaitingApproval_builds_send_to_approval_only()
    {
        var only = Assert.Single(AdjustmentMapping.BuildTransitions("AwaitingApproval", Created, Updated, null, null));
        Assert.Equal("SendToApproval", only.Action);
        Assert.Equal("AwaitingApproval", only.ToStatus);
    }

    [Fact]
    public void AwaitingTriage_has_no_transition()
        => Assert.Empty(AdjustmentMapping.BuildTransitions("AwaitingTriage", Created, Updated, null, null));
}
