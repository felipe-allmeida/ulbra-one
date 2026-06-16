using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using CPCreatePayable = Ulbra.One.Finance.Application.Endpoints.Payables.CreatePayable.CreatePayable;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UpdatePayable;

public sealed class UpdatePayable : IEndpoint
{
    public sealed record Request
    {
        public VendorId VendorId { get; set; }
        public CostCenterId CostCenterId { get; set; }
        public ExpenseCategoryId? CategoryId { get; set; }
        [NormalizeText] public string Description { get; set; } = string.Empty;
        [NormalizeText] public string? DocumentNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public List<CPCreatePayable.InstallmentInput>? Installments { get; set; }
        public CPCreatePayable.GenerateInput? Generate { get; set; }
        public ChartAccountId? AccountingAccountId { get; set; }
        public ChartAccountId? FinancialAccountId { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdatePayable").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, Request req,
        [FromKeyedServices("finance")] IUnitOfWork uow, [FromKeyedServices("core")] IUnitOfWork core,
        [FromKeyedServices("registry")] IUnitOfWork registry, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");
        if (p.Installments.Any(i => i.Payments.Any(pay => !pay.IsReversed)))
            return TypedResults.Problem(detail: "Cannot edit a payable that already has registered payments.", statusCode: 400, title: "Payable.HasPayments");

        var cc = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == p.CompanyId, ct);
        if (cc is null) return TypedResults.Problem(detail: "Cost center is invalid for this company.", statusCode: 400, title: "Payable.CostCenterInvalid");
        var vendor = await registry.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == req.VendorId.Value && v.CompanyId == p.CompanyId, ct);
        if (vendor is null) return TypedResults.Problem(detail: "Vendor is invalid for this company.", statusCode: 400, title: "Payable.VendorInvalid");

        if (req.CategoryId is { } catId)
        {
            var catOk = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Id == catId.Value && c.Active, ct);
            if (!catOk) return TypedResults.Problem(detail: "Category is invalid.", statusCode: 400, title: "Payable.CategoryInvalid");
        }

        List<Installment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return TypedResults.Problem(detail: "Invalid installment generation.", statusCode: 400, title: "Payable.InstallmentsInvalid");
            installments = Payable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return TypedResults.Problem(detail: "Each installment amount must be greater than zero.", statusCode: 400, title: "Payable.InstallmentsInvalid");
            installments = rows.Select((r, i) => new Installment(i + 1, r.DueDate, r.Amount)).ToList();
        }
        else return TypedResults.Problem(detail: "Provide installments or a generation spec.", statusCode: 400, title: "Payable.InstallmentsInvalid");

        var oldInstallments = p.Installments.ToList();
        try { p.Update(vendor.Id, vendor.Name, req.DocumentNumber, req.IssueDate, req.Description, cc.Id, req.CategoryId?.Value, installments); }
        catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 409, title: "Payable.NotEditable"); }

        if (req.AccountingAccountId is not null || req.FinancialAccountId is not null)
        {
            var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, req.FinancialAccountId?.Value, ct);
            if (err is not null) return TypedResults.Problem(detail: "Invalid chart account.", statusCode: 400, title: err);
            p.Classify(req.AccountingAccountId?.Value, req.FinancialAccountId?.Value);
        }
        // EF does not auto-track changes to the private List<T> backing field, so we manage the
        // installment set explicitly: delete old rows and register new ones.
        uow.RemoveRange(oldInstallments);
        foreach (var inst in p.Installments)
            await uow.AddAsync(inst, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
