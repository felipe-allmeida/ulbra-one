using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AccountingView;

/// <summary>Composite read model for the accounting stage screen ("Registro de Despesa"): payable money
/// picture + ledger classification + retentions + vendor tax block + fiscal document, in one call.</summary>
public sealed class GetPayableAccountingView : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/accounting-view", Handle).WithName("GetPayableAccountingView").WithTags("Finance")
            .WithSummary("Gets the accounting view of a payable.")
            .WithDescription("Everything the accounting stage screen needs in one call: the money picture, the ledger "
                + "classification, the withholdings, the vendor tax block and the fiscal document. A composite "
                + "read model, so it saves the screen from fanning out to half a dozen endpoints.")
            .RequirePermission(Permissions.PayablesRead).Produces<PayableAccountingViewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, IUnitOfWork uow, CancellationToken ct)
    {
        // Withholdings + installments are AutoInclude'd on Payable (see PayableConfiguration).
        var p = await uow.ReadSet<Payable>().AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // Fiscal document of record (1:1 per payable, unique PayableId).
        var doc = await uow.ReadSet<FiscalDocument>().FirstOrDefaultAsync(d => d.PayableId == p.Id, ct);

        // "Has document" gate (mirrors PayableFiscalGate): a parsed FiscalDocument or a Nota Fiscal attachment,
        // and for Conta Fixa a Fatura also qualifies (Fatura OU NF). A plain boleto/quote never counts.
        var hasDocument = await PayableFiscalGate.HasQualifyingDocumentAsync(uow, p, ct);

        // Vendor tax block — cross-module read by loose Guid VendorId (same pattern as GetPayable).
        var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == p.VendorId, ct);
        var vendorBlock = vendor is null
            ? null
            : new PayableVendorTaxBlock(vendor.PersonType, vendor.Name, vendor.TradeName, vendor.Document,
                vendor.Email, vendor.Phone, vendor.TaxRegime,
                vendor.SimplesAnexo, vendor.CprbForeseen, vendor.Address.City, vendor.Address.State);

        // Ledger account code/name from the global chart (Accounting module, single DbContext).
        string? accCode = null, accName = null;
        if (p.AccountingAccountId is { } accId)
        {
            var acc = await uow.ReadSet<LedgerAccount>()
                .Where(a => a.Id == accId)
                .Select(a => new { a.Code, a.Name })
                .FirstOrDefaultAsync(ct);
            accCode = acc?.Code;
            accName = acc?.Name;
        }

        return TypedResults.Ok(new PayableAccountingViewResponse(
            new PayableId(p.Id),
            p.Status,
            p.Origin,
            p.CounterpartyName,
            p.DocumentNumber,
            p.TotalAmount,
            p.TotalWithheld,
            p.NetTotal,
            p.AccountingAccountId is { } a ? new LedgerAccountId(a) : null,
            accCode,
            accName,
            p.Withholdings.Select(PayableWithholdingResponse.From).ToList(),
            vendorBlock,
            doc is null ? null : FiscalDocumentResponse.From(doc),
            hasDocument));
    }
}
