using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// The título a reconciled statement line is tied to — the other half of the pair, so the screen can show
/// "conta a pagar × extrato" side by side instead of merely asserting that the line was reconciled.
/// Present only on reconciled lines; an ignored line has no título behind it.
/// </summary>
[SwaggerSchema("The payable side of a reconciled statement line, for showing the pairing.")]
public sealed record ConciliationMatchedTitleResponse(
    [property: SwaggerSchema("Payable the payment settled, so the screen can link straight to it.")]
    PayableId PayableId,
    [property: SwaggerSchema("Vendor on the payable — the name an operator recognises.")]
    [property: SwaggerExample("AR SMARTVIX SERVICOS DE CERTIFICACAO DIGITAL LTDA")]
    string CounterpartyName,
    [property: SwaggerSchema("Document number of the payable, when it has one.")]
    [property: SwaggerExample("NF 12345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Which installment of the payable was settled, 1-based.")]
    int Sequence,
    [property: SwaggerSchema("How many installments the payable has in total.")]
    int InstallmentCount,
    [property: SwaggerSchema("Due date of the settled installment — the date to compare against the statement day.")]
    DateOnly DueDate,
    [property: SwaggerSchema("Amount of the payment that settled it.")]
    decimal Amount,
    [property: SwaggerSchema("Date the payment was registered under (the bank's effective date on a CNAB baixa).")]
    DateOnly PaymentDate,
    [property: SwaggerSchema("Free-text note the settling flow wrote — e.g. \"CNAB retorno\" or \"Conciliação: …\".")]
    string? Notes);
