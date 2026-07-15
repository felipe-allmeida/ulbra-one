using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetWithholdings;

public sealed class SetWithholdings : IEndpoint
{
    public sealed record Request
    {
        public List<Item>? Items { get; set; }

        public sealed record Item
        {
            public WithholdingTaxType TaxType { get; set; }
            public decimal BaseAmount { get; set; }
            public decimal? Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/withholdings", Handle)
            .WithName("SetWithholdings").WithTags("Finance")
            .RequirePermission(Permissions.PayablesWrite)
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        if (p.Installments.Any(i => i.Payments.Any(pay => !pay.IsReversed)))
            return TypedResults.Problem(
                detail: "Cannot change withholdings on a payable that already has registered payments.",
                statusCode: 400,
                title: "Payable.HasPayments");

        // Capture old withholdings before mutation so EF can remove the stale rows.
        var oldWithholdings = p.Withholdings.ToList();

        try
        {
            p.SetWithholdings((req.Items ?? []).Select(i => (i.TaxType, i.BaseAmount, i.Rate, i.Amount)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "Payable.WithholdingsInvalid");
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
