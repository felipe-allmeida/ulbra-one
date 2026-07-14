using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Withholdings;

/// <summary>A service line — the calculation base. Built only for services (the caller gates on service-nature).
/// A null Lc116Subitem means the specific subitem is unknown: the general federal taxes still apply.</summary>
public sealed record ServiceLineInput(string? Lc116Subitem, string? MunicipalityIbge, decimal? InvoiceIssRate, decimal Amount);

/// <summary>Inputs for the withholding calculation, assembled by Finance from the payable + vendor + fiscal document.</summary>
public sealed record WithholdingContext(
    Guid CompanyId, Guid VendorId, TaxRegime VendorRegime, IReadOnlyList<ServiceLineInput> Lines,
    // INSS: Cprb = "is CPRB stated on the invoice?" confirmed on the payable; SimplesAnexo = the vendor's Simples annex.
    bool? Cprb = null, SimplesAnexo? SimplesAnexo = null,
    // Withholding rates configured on the vendor (per tax): replace the rule's rate when the tax is withheld.
    IReadOnlyDictionary<WithholdingTaxType, decimal>? RateOverrides = null);

/// <summary>A computed withholding line (same shape Payable.SetWithholdings consumes).</summary>
public sealed record WithholdingResult(WithholdingTaxType TaxType, decimal BaseAmount, decimal Rate, decimal Amount);

/// <summary>Port: computes the withholdings for a payable's service lines. Defined in Finance, implemented by the Fiscal module.</summary>
public interface IWithholdingCalculator
{
    Task<IReadOnlyList<WithholdingResult>> CalculateAsync(WithholdingContext context, CancellationToken ct);
}
