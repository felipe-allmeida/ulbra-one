using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Withholdings;

/// <summary>A service line from the fiscal document — the calculation base. A null Lc116Subitem = product (no withholding).</summary>
public sealed record ServiceLineInput(string? Lc116Subitem, string? MunicipalityIbge, decimal? NotaIssRate, decimal Amount);

/// <summary>Inputs for the withholding calculation, assembled by Finance from the payable + vendor + fiscal document.</summary>
public sealed record WithholdingContext(
    Guid CompanyId, Guid VendorId, TaxRegime VendorRegime, bool SimplesDeclarationProvided, IReadOnlyList<ServiceLineInput> Lines);

/// <summary>A computed withholding line (same shape Payable.SetWithholdings consumes).</summary>
public sealed record WithholdingResult(WithholdingTaxType TaxType, decimal BaseAmount, decimal Rate, decimal Amount);

/// <summary>Port: computes the withholdings for a payable's service lines. Defined in Finance, implemented by the Fiscal module.</summary>
public interface IWithholdingCalculator
{
    Task<IReadOnlyList<WithholdingResult>> CalculateAsync(WithholdingContext context, CancellationToken ct);
}
