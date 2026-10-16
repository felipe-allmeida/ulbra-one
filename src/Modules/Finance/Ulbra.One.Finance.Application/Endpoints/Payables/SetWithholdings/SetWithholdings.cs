using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetWithholdings;

public sealed class SetWithholdings : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("The payable's complete set of withholdings. It REPLACES the current set — an "
            + "omitted tax is removed, and an empty list clears them all.")]
        public List<Item>? Items { get; set; }

        public sealed record Item
        {
            [SwaggerSchema("Which tax is withheld.")]
            public WithholdingTaxType TaxType { get; set; }

            [SwaggerSchema("Amount the rate was applied to, in BRL. It is not necessarily the payable's "
                + "total — each tax has its own base rule.")]
            [SwaggerExample("10000.00")]
            public decimal BaseAmount { get; set; }

            [SwaggerSchema("Rate applied, as a percentage. Informational: Amount is what is withheld, and "
                + "it is not recomputed from base times rate.")]
            [SwaggerExample("4.65")]
            public decimal? Rate { get; set; }

            [SwaggerSchema("Amount actually withheld, in BRL. This is the authoritative figure — it is "
                + "what reduces the net payment and what the fiscal assessment reports.")]
            [SwaggerExample("465.00")]
            public decimal Amount { get; set; }
        }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/withholdings", Handle)
            .WithName("SetWithholdings").WithTags("Finance")
            .WithSummary("Sets a payable's withholdings.")
            .WithDescription("Replaces the whole set of source withholdings on the payable. These are what "
                + "reduce the net amount paid to the vendor and what the fiscal assessment and EFD-Reinf "
                + "export read, so they are fiscal data, not a note. The rules engine suggests them, but "
                + "the amounts sent here are taken as authoritative.")
            .RequirePermission(Permissions.PayablesTax)
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null)
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        if (p.Installments.Any(i => i.Payments.Any(pay => !pay.IsReversed)))
            return ErrorResults.InvalidRequest("Cannot change withholdings on a payable that already has registered payments.", ErrorCodes.Payable.HasPayments);

        // Capture old withholdings before mutation so EF can remove the stale rows.
        var oldWithholdings = p.Withholdings.ToList();

        try
        {
            p.SetWithholdings((req.Items ?? []).Select(i => (i.TaxType, i.BaseAmount, i.Rate, i.Amount)));
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.Payable.WithholdingsInvalid);
        }

        // EF does not auto-track changes to the private List<T> backing field, so manage the
        // withholding child-set explicitly: remove old rows, add new ones.
        // The installment NetAmount scalar mutations persist automatically (installments are tracked).
        uow.RemoveRange(oldWithholdings);
        foreach (var w in p.Withholdings)
            await uow.AddAsync(w, ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
