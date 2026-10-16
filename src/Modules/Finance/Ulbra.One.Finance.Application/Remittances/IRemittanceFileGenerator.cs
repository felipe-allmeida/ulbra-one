using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Port that turns a remittance batch into the bank-specific payment-file bytes (CNAB240 today).
/// Pure: no I/O, no storage — the caller persists the returned bytes.
/// </summary>
public interface IRemittanceFileGenerator
{
    /// <summary>Builds the CNAB240 remittance bytes for the given batch. Pure: no I/O.</summary>
    byte[] Generate(RemittanceFileInput input);
}

/// <summary>
/// Everything the file engine needs to emit a remittance, decoupled from the
/// <c>Remittance</c> aggregate so the engine stays pure and independently testable.
/// </summary>
/// <param name="BankCode">COMPE code of the paying bank (e.g. <c>033</c>). The profile owns the authoritative value.</param>
/// <param name="SequenceNumber">NSA — the file's sequential number, incremented per generated file.</param>
/// <param name="GeneratedAt">File generation timestamp (date + time written into the Header de Arquivo).</param>
public sealed record RemittanceFileInput(
    string BankCode,
    int SequenceNumber,
    DateTimeOffset GeneratedAt,
    RemittancePayerInfo Payer,
    IReadOnlyList<RemittanceFileLine> Lines);

/// <summary>The paying company's bank/debit-account data, written into every record's company fields.</summary>
/// <param name="Cnpj">Paying company CNPJ, digits only.</param>
/// <param name="LegalName">Paying company legal name.</param>
/// <param name="Agency">Debit-account agency (no DV).</param>
/// <param name="AccountNumber">Debit account number (no DV).</param>
/// <param name="AccountDigit">Debit account verifier digit.</param>
/// <param name="AgreementCode">Santander 12-digit "convênio" code (composed with bank + agency into the 20-byte field).</param>
public sealed record RemittancePayerInfo(
    string Cnpj,
    string LegalName,
    string Agency,
    string AccountNumber,
    string AccountDigit,
    string AgreementCode,
    string AgencyDigit = "",
    string AddressStreet = "",
    string AddressNumber = "",
    string AddressComplement = "",
    string AddressCity = "",
    string AddressCep = "",
    string AddressState = "");

/// <summary>
/// A single payment instruction to emit. PIX/TED expand to Segmento A + B; boleto bancário to
/// Segmento J + J-52; convênio/tributo to Segmento O (routed by <see cref="BoletoDigitableLine"/>).
/// </summary>
/// <param name="Reference">Round-trip control id echoed back on retorno ("Seu Número", 20 chars max). Never truncated.</param>
/// <param name="BoletoDigitableLine">
/// Boleto/arrecadação linha digitável (47 díg bancário / 48 díg convênio), digits only or formatted.
/// Required for <see cref="PaymentMethod.Boleto"/>; null for PIX/TED.
/// </param>
/// <param name="DueDate">Título due date (Seg J/O vencimento; Seg B não-PIX). Falls back to PaymentDate when null.</param>
public sealed record RemittanceFileLine(
    PaymentMethod Method,
    decimal Amount,
    DateOnly PaymentDate,
    string FavoredName,
    string FavoredDocument,
    string FavoredBankCode,
    string FavoredAgency,
    string FavoredAccount,
    string FavoredAccountDigit,
    string FavoredPixKey,
    string Reference,
    string? BoletoDigitableLine = null,
    DateOnly? DueDate = null,
    string FavoredAddressStreet = "",
    string FavoredAddressNumber = "",
    string FavoredAddressComplement = "",
    string FavoredAddressNeighborhood = "",
    string FavoredAddressCity = "",
    string FavoredAddressCep = "",
    string FavoredAddressState = "",
    // PIX QR dinâmico (copia e cola, forma 47): URL de endereçamento extraída do EMV (Seg J52-PIX 132-210)
    // e nome do recebedor (tag 59). Não-nulo/não-vazio ⇒ a linha vai por Seg J-PIX + J52-PIX em vez de A+B.
    string? PixQrDynamicUrl = null,
    string? PixQrReceiverName = null);
