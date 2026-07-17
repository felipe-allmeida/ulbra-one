using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record StatementImportResponse(
    StatementImportId Id, CompanyId CompanyId, BankAccountId BankAccountId, StatementSource Source, string FileName,
    DateOnly? PeriodStart, DateOnly? PeriodEnd, int TransactionCount, int NewCount, int DuplicateCount,
    ImportStatus Status, DateTimeOffset CreatedAt, string? Warning = null)
{
    public static StatementImportResponse From(BankStatementImport i, string? warning = null) => new(
        new StatementImportId(i.Id), new CompanyId(i.CompanyId), new BankAccountId(i.BankAccountId), i.Source, i.FileName,
        i.PeriodStart, i.PeriodEnd, i.TransactionCount, i.NewCount, i.DuplicateCount, i.Status, i.CreatedAt, warning);
}
