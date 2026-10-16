using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("One installment inside a CNAB remittance file. Its status tracks what the BANK said "
    + "about it, which is what the return file updates.")]
public sealed record RemittanceLineResponse(
    [property: SwaggerSchema("Identifier of the remittance line.")]
    Guid Id,
    [property: SwaggerSchema("Payable behind this line.")]
    Guid PayableId,
    [property: SwaggerSchema("Installment being remitted.")]
    Guid InstallmentId,
    [property: SwaggerSchema("Payment method declared in the file for this line.")]
    [property: SwaggerExample("Ted")]
    string Method,
    [property: SwaggerSchema("Amount to transfer, in BRL — the installment's net amount.")]
    [property: SwaggerExample("1430.25")]
    decimal Amount,
    [property: SwaggerSchema("Beneficiary name as written into the file.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string FavoredName,
    [property: SwaggerSchema("Beneficiary CNPJ or CPF, digits only.")]
    [property: SwaggerExample("12345678000195")]
    string FavoredDocument,
    [property: SwaggerSchema("Our reference carried in the file — how the return file is matched back to "
        + "this line.")]
    [property: SwaggerExample("PAG-0004821")]
    string Reference,
    [property: SwaggerSchema("What the bank said about this line: still pending, paid, or rejected. It "
        + "only moves when a return file is imported.")]
    [property: SwaggerExample("Pending")]
    string Status,
    [property: SwaggerSchema("Date the bank actually settled the line, from the return file. Null while "
        + "no return has been processed.")]
    [property: SwaggerExample("2026-08-09")]
    DateOnly? EffectiveDate,
    [property: SwaggerSchema("The bank's own reference or rejection code, from the return file. On a "
        + "rejected line this is what says why.")]
    string? BankReference,
    [property: SwaggerSchema("Título due date (vencimento). Null for PIX lines.")]
    [property: SwaggerExample("2026-09-10")]
    DateOnly? DueDate,
    [property: SwaggerSchema("Boleto/arrecadação linha digitável, when the line is paid by boleto. Null "
        + "for PIX/TED.")]
    string? BoletoDigitableLine,
    [property: SwaggerSchema("Reason/description from the return file when the line was rejected. Null "
        + "otherwise.")]
    string? ReturnReason,
    [property: SwaggerSchema("Unit (Unidade) of the underlying payable, resolved for display. Null when "
        + "the payable has no unit or the response was not enriched.")]
    Guid? OriginUnitId,
    [property: SwaggerSchema("Unit display name. Null when there is no unit or the response was not enriched.")]
    string? UnitName,
    [property: SwaggerSchema("Cost center (Natureza) of the underlying payable. Null when not enriched.")]
    Guid? CostCenterId,
    [property: SwaggerSchema("Cost center display name (Natureza). Null when not enriched.")]
    string? NatureName)
{
    public static RemittanceLineResponse From(RemittanceLine line, RemittanceLinePayableContext? ctx = null) => new(
        line.Id,
        line.PayableId,
        line.InstallmentId,
        line.Method.ToString(),
        line.Amount,
        line.FavoredName,
        line.FavoredDocument,
        line.Reference,
        line.Status.ToString(),
        line.EffectiveDate,
        line.BankReference,
        line.DueDate,
        line.BoletoDigitableLine,
        line.ReturnReason,
        ctx?.OriginUnitId,
        ctx?.UnitName,
        ctx?.CostCenterId,
        ctx?.NatureName);
}

/// <summary>Resolved unit/cost-center of a remittance line's underlying payable, used to enrich the line
/// view (unit/nature do not live on the line itself). Null fields mean "not resolved".</summary>
public sealed record RemittanceLinePayableContext(Guid? OriginUnitId, string? UnitName, Guid? CostCenterId, string? NatureName);

[SwaggerSchema("A CNAB remittance — the payment file sent to the bank, and its lines. Generating it "
    + "does not pay anything: the money only moves when the bank processes the file and the return "
    + "confirms it.")]
public sealed record RemittanceResponse(
    [property: SwaggerSchema("Identifier of the remittance.")]
    Guid Id,
    [property: SwaggerSchema("Company the remittance belongs to.")]
    Guid CompanyId,
    [property: SwaggerSchema("Bank account the payments go out of. Its CNAB profile is what the file "
        + "header is built from.")]
    Guid PayingBankAccountId,
    [property: SwaggerSchema("Bank code (COMPE) the file was laid out for. The layout is bank-specific, "
        + "so a file cannot be sent to a different bank.")]
    [property: SwaggerExample("341")]
    string BankCode,
    [property: SwaggerSchema("Sequential number of the file for this account, as the bank expects it.")]
    [property: SwaggerExample("42")]
    int SequenceNumber,
    [property: SwaggerSchema("Where the remittance stands: generated and awaiting the bank, or cancelled.")]
    [property: SwaggerExample("Generated")]
    string Status,
    [property: SwaggerSchema("Payment-form category of this file (Pix/Ted/BoletoBancario/Convenio). Null on "
        + "pre-Onda-6 files that mixed forms in one file.")]
    [property: SwaggerExample("BoletoBancario")]
    string? Category,
    [property: SwaggerSchema("Storage key of the generated file. Used by the download endpoint, not "
        + "directly fetchable.")]
    string? FileStorageKey,
    [property: SwaggerSchema("File name as the operator downloads it.")]
    [property: SwaggerExample("CNAB240_341_000042.REM")]
    string? FileName,
    [property: SwaggerSchema("File size in bytes.")]
    [property: SwaggerExample("18240")]
    long? FileSizeBytes,
    [property: SwaggerSchema("When the file was generated.")]
    DateTimeOffset GeneratedAt,
    [property: SwaggerSchema("Date the bank was instructed to execute the payments (CNAB data_pagamento). "
        + "Equals the generation date unless the batch was scheduled ahead.")]
    [property: SwaggerExample("2026-09-15")]
    DateOnly PaymentDate,
    [property: SwaggerSchema("Who generated it.")]
    [property: SwaggerExample("redacted-12@example.invalid")]
    string GeneratedBy,
    [property: SwaggerSchema("Sum of the lines, in BRL.")]
    [property: SwaggerExample("184320.75")]
    decimal TotalAmount,
    [property: SwaggerSchema("The installments in the file.")]
    IReadOnlyList<RemittanceLineResponse> Lines)
{
    /// <summary>Maps a remittance to its response. Pass <paramref name="ctxByPayable"/> to enrich each line
    /// with the underlying payable's unit/cost-center (drill-down view); omit it for the plain shape.</summary>
    public static RemittanceResponse From(
        Remittance remittance, IReadOnlyDictionary<Guid, RemittanceLinePayableContext>? ctxByPayable = null) => new(
        remittance.Id,
        remittance.CompanyId,
        remittance.PayingBankAccountId,
        remittance.BankCode,
        remittance.SequenceNumber,
        remittance.Status.ToString(),
        remittance.Category?.ToString(),
        remittance.FileStorageKey,
        remittance.FileName,
        remittance.FileSizeBytes,
        remittance.GeneratedAt,
        remittance.PaymentDate,
        remittance.GeneratedBy,
        remittance.TotalAmount,
        remittance.Lines.Select(l => RemittanceLineResponse.From(
            l, ctxByPayable != null && ctxByPayable.TryGetValue(l.PayableId, out var c) ? c : null)).ToList());
}

[SwaggerSchema("An installment that was asked for but left out of the file, with the reason. It is not "
    + "an error — the remittance is generated with what qualifies, and these need fixing separately.")]
public sealed record SkippedInstallmentResponse(
    [property: SwaggerSchema("Payable behind the skipped installment.")]
    Guid PayableId,
    [property: SwaggerSchema("The installment that was skipped.")]
    Guid InstallmentId,
    [property: SwaggerSchema("Why it was left out — unsupported bank, incomplete vendor bank data, "
        + "already in another remittance.")]
    [property: SwaggerExample("Vendor bank data is incomplete.")]
    string Reason)
{
    public static SkippedInstallmentResponse From(SkippedInstallment s) => new(s.PayableId, s.InstallmentId, s.Reason);
}

[SwaggerSchema("What a remittance generation produced: one file per payment form present in the selection "
    + "(AEL-933), plus the installments it could not include. A non-empty Skipped list is the normal case, "
    + "not a failure.")]
public sealed record CreateRemittanceResponse(
    [property: SwaggerSchema("The remittances generated — one per payment form present in the selection "
        + "(Pix/Ted/boleto/convênio).")]
    IReadOnlyList<RemittanceResponse> Remittances,
    [property: SwaggerSchema("Installments left out, each with its reason.")]
    IReadOnlyList<SkippedInstallmentResponse> Skipped);

[SwaggerSchema("A remittance as it appears in the list — headline fields only, without the lines.")]
public sealed record RemittanceListItemResponse(
    [property: SwaggerSchema("Identifier of the remittance.")]
    Guid Id,
    [property: SwaggerSchema("Sequential number of the file for its account.")]
    [property: SwaggerExample("42")]
    int SequenceNumber,
    [property: SwaggerSchema("Where the remittance stands.")]
    [property: SwaggerExample("Generated")]
    string Status,
    [property: SwaggerSchema("Payment-form category of this file (Pix/Ted/BoletoBancario/Convenio). Null on "
        + "pre-Onda-6 mixed files.")]
    [property: SwaggerExample("BoletoBancario")]
    string? Category,
    [property: SwaggerSchema("How many installments are in the file.")]
    [property: SwaggerExample("128")]
    int LineCount,
    [property: SwaggerSchema("Sum of the lines, in BRL.")]
    [property: SwaggerExample("184320.75")]
    decimal TotalAmount,
    [property: SwaggerSchema("When the file was generated.")]
    DateTimeOffset GeneratedAt,
    [property: SwaggerSchema("Date the bank was instructed to execute the payments. Equals the generation "
        + "date unless the batch was scheduled ahead.")]
    [property: SwaggerExample("2026-09-15")]
    DateOnly PaymentDate)
{
    public static RemittanceListItemResponse From(Remittance remittance) => new(
        remittance.Id,
        remittance.SequenceNumber,
        remittance.Status.ToString(),
        remittance.Category?.ToString(),
        remittance.Lines.Count,
        remittance.TotalAmount,
        remittance.GeneratedAt,
        remittance.PaymentDate);
}
