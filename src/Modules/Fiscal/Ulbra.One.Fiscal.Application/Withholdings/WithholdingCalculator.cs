using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Fiscal.Domain.Withholdings;

namespace Ulbra.One.Fiscal.Application.Withholdings;

/// <summary>Fiscal-side implementation of Finance's withholding port: loads the applicable rules and runs the pure resolver.</summary>
public sealed class WithholdingCalculator(IUnitOfWork uow) : IWithholdingCalculator
{
    public async Task<IReadOnlyList<WithholdingResult>> CalculateAsync(WithholdingContext context, CancellationToken ct)
    {
        // Company-specific rules + group-wide rules (CompanyId == null).
        var rules = await uow.ReadSet<WithholdingRule>()
            .Where(r => r.CompanyId == null || r.CompanyId == context.CompanyId)
            .ToListAsync(ct);

        var resolutionContext = new ResolutionContext(
            context.CompanyId,
            context.VendorRegime,
            context.Lines
                .Select(l => new ServiceLine(l.Lc116Subitem, l.MunicipalityIbge, l.NotaIssRate, l.Amount))
                .ToList());

        return WithholdingRuleResolver.Resolve(rules, resolutionContext)
            .Select(c => new WithholdingResult(c.TaxType, c.BaseAmount, c.Rate, c.Amount))
            .ToList();
    }
}
