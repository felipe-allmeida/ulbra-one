using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("The result of importing a bank statement. Re-importing an overlapping file is safe: "
    + "lines already present are counted as duplicates rather than added again.")]
public sealed record StatementImportResponse(
    StatementImportId Id,
    [property: SwaggerSchema("Company the statement belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Account the statement was imported into.")]
    BankAccountId BankAccountId,
    [property: SwaggerSchema("Format of the imported file.")]
    StatementSource Source,
    [property: SwaggerSchema("Name of the imported file.")]
    [property: SwaggerExample("extrato-341-2026-08.ofx")]
    string FileName,
    [property: SwaggerSchema("First date covered by the file. Null when it could not be determined.")]
    [property: SwaggerExample("2026-08-01")]
    DateOnly? PeriodStart,
    [property: SwaggerSchema("Last date covered by the file.")]
    [property: SwaggerExample("2026-08-31")]
    DateOnly? PeriodEnd,
    [property: SwaggerSchema("Lines found in the file.")]
    [property: SwaggerExample("184")]
    int TransactionCount,
    [property: SwaggerSchema("Lines actually added — these are the ones that reach reconciliation.")]
    [property: SwaggerExample("162")]
    int NewCount,
    [property: SwaggerSchema("Lines already present from an earlier import and therefore skipped. A high "
        + "count on a re-import is expected, not a problem.")]
    [property: SwaggerExample("22")]
    int DuplicateCount,
    [property: SwaggerSchema("How the import ended.")]
    ImportStatus Status,
    [property: SwaggerSchema("When the file was imported.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Warning about something odd that did not stop the import — an account id in "
        + "the file that differs from the selected account, for instance.")]
    string? Warning = null)
{
    public static StatementImportResponse From(BankStatementImport i, string? warning = null) => new(
        new StatementImportId(i.Id), new CompanyId(i.CompanyId), new BankAccountId(i.BankAccountId), i.Source, i.FileName,
        i.PeriodStart, i.PeriodEnd, i.TransactionCount, i.NewCount, i.DuplicateCount, i.Status, i.CreatedAt, warning);
}
