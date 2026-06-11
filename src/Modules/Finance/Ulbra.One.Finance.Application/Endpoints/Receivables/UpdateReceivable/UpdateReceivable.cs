using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Normalization;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using CRCreateReceivable = Ulbra.One.Finance.Application.Endpoints.Receivables.CreateReceivable.CreateReceivable;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.UpdateReceivable;

public sealed class UpdateReceivable : IEndpoint
{
    public sealed record UpdateReceivableRequest
    {
        public CustomerId PayerId { get; set; }
        public CostCenterId CostCenterId { get; set; }
        public Guid? RevenueCategoryId { get; set; }
        public Guid? LedgerAccountId { get; set; }
        [NormalizeText] public string Description { get; set; } = string.Empty;
        [NormalizeText] public string? DocumentNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public List<CRCreateReceivable.ReceivableInstallmentInput>? Installments { get; set; }
        public CRCreateReceivable.ReceivableGenerateInput? Generate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateReceivable").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<UpdateReceivableRequest>()
            .Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, UpdateReceivableRequest req,
        [FromKeyedServices("finance")] IUnitOfWork uow, [FromKeyedServices("core")] IUnitOfWork core,
        [FromKeyedServices("registry")] IUnitOfWork registry, TimeProvider clock, CancellationToken ct)
    {
        var r = await uow.Set<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (r is null) return Results.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");

        var cc = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == r.CompanyId, ct);
        if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.", statusCode: 400, title: "Receivable.CostCenterInvalid");
        var customer = await registry.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == r.CompanyId, ct);
        if (customer is null) return Results.Problem(detail: "Customer is invalid for this company.", statusCode: 400, title: "Receivable.CustomerInvalid");

        List<ReceivableInstallment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return Results.Problem(detail: "Invalid installment generation.", statusCode: 400, title: "Receivable.InstallmentsInvalid");
            installments = Receivable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(x => x.Amount <= 0))
                return Results.Problem(detail: "Each installment amount must be greater than zero.", statusCode: 400, title: "Receivable.InstallmentsInvalid");
            installments = rows.Select((x, i) => new ReceivableInstallment(i + 1, x.DueDate, x.Amount)).ToList();
        }
        else return Results.Problem(detail: "Provide installments or a generation spec.", statusCode: 400, title: "Receivable.InstallmentsInvalid");

        var oldInstallments = r.Installments.ToList();
        try { r.Update(customer.Id, customer.Name, req.DocumentNumber, req.IssueDate, req.Description, cc.Id, req.RevenueCategoryId, req.LedgerAccountId, installments); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receivable.NotEditable"); }
        // EF does not auto-track changes to the private List<T> backing field, so we manage the
        // installment set explicitly: delete old rows and register new ones.
        uow.RemoveRange(oldInstallments);
        foreach (var inst in r.Installments)
            await uow.AddAsync(inst, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ReceivableResponse.From(r, ReceivableClock.Today(clock)));
    }
}
