using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Application.Conciliation;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("An open installment near enough to a statement line to be worth showing next to it — either "
    + "the suggestion the matcher made, or a near miss that explains why it made none. The two deltas say how "
    + "far it sits from the line, so an operator can judge the pairing without opening the título.")]
public sealed record ConciliationCandidateResponse(
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Payable the installment belongs to, so the screen can link straight to it.")]
    PayableId PayableId,
    [property: SwaggerSchema("Vendor on the payable — the name an operator recognises.")]
    [property: SwaggerExample("CONSTRUTORA HORIZONTE LTDA")]
    string CounterpartyName,
    [property: SwaggerSchema("Document number of the payable, when it has one.")]
    [property: SwaggerExample("NF 12345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Which installment of the payable this is, 1-based.")]
    int Sequence,
    [property: SwaggerSchema("How many installments the payable has in total.")]
    int InstallmentCount,
    [property: SwaggerSchema("Outstanding balance of the installment — net of withholdings and of anything "
        + "already paid. This, not the gross amount, is what a bank debit has to match.")]
    [property: SwaggerExample("442.36")]
    decimal Remaining,
    [property: SwaggerSchema("Due date of the installment.")]
    [property: SwaggerExample("2026-08-25")]
    DateOnly DueDate,
    [property: SwaggerSchema("Absolute gap between the statement amount and the outstanding balance. Zero on an "
        + "exact match; anything else is juros, desconto or a partial payment.")]
    [property: SwaggerExample("10.85")]
    decimal AmountDelta,
    [property: SwaggerSchema("Absolute gap in days between the statement date and the due date.")]
    [property: SwaggerExample("2")]
    int DayDelta)
{
    /// <summary>Joins a scored near-miss to the payable fields the screen needs to render it.</summary>
    public static ConciliationCandidateResponse From(CandidateNearMiss c, CandidateDisplay d) => new(
        new InstallmentId(c.InstallmentId), new PayableId(d.PayableId), d.CounterpartyName, d.DocumentNumber,
        d.Sequence, d.InstallmentCount, c.Remaining, c.DueDate, c.AmountDelta, c.DayDelta);
}

/// <summary>The payable-side fields of a candidate installment, looked up alongside the matching pool.
/// Kept out of <see cref="InstallmentCandidate"/> so the matcher stays a pure value comparison.</summary>
public sealed record CandidateDisplay(
    Guid PayableId, string CounterpartyName, string? DocumentNumber, int Sequence, int InstallmentCount);
