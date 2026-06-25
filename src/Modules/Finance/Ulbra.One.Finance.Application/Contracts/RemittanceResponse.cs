using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record RemittanceLineResponse(
    Guid Id,
    Guid PayableId,
    Guid InstallmentId,
    string Method,
    decimal Amount,
    string FavoredName,
    string FavoredDocument,
    string Reference,
    string Status)
{
    public static RemittanceLineResponse From(RemittanceLine line) => new(
        line.Id,
        line.PayableId,
        line.InstallmentId,
        line.Method.ToString(),
        line.Amount,
        line.FavoredName,
        line.FavoredDocument,
        line.Reference,
        line.Status.ToString());
}

public sealed record RemittanceResponse(
    Guid Id,
    Guid CompanyId,
    Guid PayingBankAccountId,
    string BankCode,
    int SequenceNumber,
    string Status,
    string? FileStorageKey,
    string? FileName,
    long? FileSizeBytes,
    DateTimeOffset GeneratedAt,
    string GeneratedBy,
    decimal TotalAmount,
    IReadOnlyList<RemittanceLineResponse> Lines)
{
    public static RemittanceResponse From(Remittance remittance) => new(
        remittance.Id,
        remittance.CompanyId,
        remittance.PayingBankAccountId,
        remittance.BankCode,
        remittance.SequenceNumber,
        remittance.Status.ToString(),
        remittance.FileStorageKey,
        remittance.FileName,
        remittance.FileSizeBytes,
        remittance.GeneratedAt,
        remittance.GeneratedBy,
        remittance.TotalAmount,
        remittance.Lines.Select(RemittanceLineResponse.From).ToList());
}

public sealed record RemittanceListItemResponse(
    Guid Id,
    int SequenceNumber,
    string Status,
    int LineCount,
    decimal TotalAmount,
    DateTimeOffset GeneratedAt)
{
    public static RemittanceListItemResponse From(Remittance remittance) => new(
        remittance.Id,
        remittance.SequenceNumber,
        remittance.Status.ToString(),
        remittance.Lines.Count,
        remittance.TotalAmount,
        remittance.GeneratedAt);
}
