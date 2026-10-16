using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One processed CNAB return file, as listed in a paying account's return history.</summary>
[SwaggerSchema("A return file the bank sent back, as it was processed. The file itself is kept, so the "
    + "counters here can always be checked against the original.")]
public sealed record RemittanceReturnHistoryItem(
    [property: SwaggerSchema("Identifier of the processed return. Use it to download the stored file.")]
    RemittanceReturnId Id,
    [property: SwaggerSchema("Name of the file as the operator uploaded it.")]
    [property: SwaggerExample("RETORNO_20260820.RET")]
    string FileName,
    [property: SwaggerSchema("When the file was processed.")]
    DateTimeOffset ProcessedAt,
    [property: SwaggerSchema("Who uploaded it.")]
    [property: SwaggerExample("maria.silva")]
    string ProcessedBy,
    [property: SwaggerSchema("Whether the settlement was applied. False means the file's trailer did not "
        + "reconcile, so nothing was settled - the file was kept anyway, which is what lets the operator "
        + "take it back to the bank.")]
    bool Applied,
    [property: SwaggerSchema("Entries matched to a known remittance line.")]
    [property: SwaggerExample("125")]
    int Matched,
    [property: SwaggerSchema("Entries settled on this upload.")]
    [property: SwaggerExample("119")]
    int Paid,
    [property: SwaggerSchema("Entries the bank rejected.")]
    [property: SwaggerExample("4")]
    int Returned,
    [property: SwaggerSchema("Entries whose reference matched nothing.")]
    [property: SwaggerExample("1")]
    int Unmatched,
    [property: SwaggerSchema("Entries left for manual review - a value divergence, or a reference that "
        + "matched more than one line.")]
    [property: SwaggerExample("2")]
    int Divergent)
{
    public static RemittanceReturnHistoryItem From(RemittanceReturn r) => new(
        new RemittanceReturnId(r.Id),
        r.FileName,
        r.ProcessedAt,
        r.ProcessedBy,
        r.Applied,
        r.MatchedCount,
        r.PaidCount,
        r.ReturnedCount,
        r.UnmatchedCount,
        r.DivergentCount);
}
