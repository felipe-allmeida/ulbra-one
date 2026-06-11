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
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.CreatePayable;

public sealed class CreatePayable : IEndpoint
{
    public sealed record InstallmentInput(DateOnly DueDate, decimal Amount);
    public sealed record GenerateInput(decimal TotalAmount, int Count, DateOnly FirstDueDate, int IntervalMonths = 1);

    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        public VendorId VendorId { get; set; }
        public CostCenterId CostCenterId { get; set; }
        public ExpenseCategoryId? CategoryId { get; set; }
        public PayableOrigin Origin { get; set; } = PayableOrigin.Manual;
        [NormalizeText] public string Description { get; set; } = string.Empty;
        [NormalizeText] public string? DocumentNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public List<InstallmentInput>? Installments { get; set; }
        public GenerateInput? Generate { get; set; }
        public ChartAccountId? AccountingAccountId { get; set; }
        public ChartAccountId? FinancialAccountId { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreatePayable").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<PayableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        Request req,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        [FromKeyedServices("core")] IUnitOfWork core,
        [FromKeyedServices("registry")] IUnitOfWork registry,
        TimeProvider clock,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;

        var cc = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == companyId, ct);
        if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.", statusCode: 400, title: "Payable.CostCenterInvalid");

        var vendor = await registry.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == req.VendorId.Value && v.CompanyId == companyId, ct);
        if (vendor is null) return Results.Problem(detail: "Vendor is invalid for this company.", statusCode: 400, title: "Payable.VendorInvalid");

        if (req.CategoryId is { } catId)
        {
            var catOk = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Id == catId.Value && c.Active, ct);
            if (!catOk) return Results.Problem(detail: "Category is invalid.", statusCode: 400, title: "Payable.CategoryInvalid");
        }

        List<Installment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return Results.Problem(detail: "Invalid installment generation.", statusCode: 400, title: "Payable.InstallmentsInvalid");
            installments = Payable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return Results.Problem(detail: "Each installment amount must be greater than zero.", statusCode: 400, title: "Payable.InstallmentsInvalid");
            installments = rows.Select((r, i) => new Installment(i + 1, r.DueDate, r.Amount)).ToList();
        }
        else
        {
            return Results.Problem(detail: "Provide either explicit installments or a generation spec.", statusCode: 400, title: "Payable.InstallmentsInvalid");
        }

        var payable = new Payable(companyId, req.Origin, vendor.Id, vendor.Name, req.DocumentNumber, req.IssueDate,
            req.Description, cc.Id, req.CategoryId?.Value, installments);

        if (req.AccountingAccountId is not null || req.FinancialAccountId is not null)
        {
            var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, req.FinancialAccountId?.Value, ct);
            if (err is not null) return Results.Problem(detail: "Invalid chart account.", statusCode: 400, title: err);
            payable.Classify(req.AccountingAccountId?.Value, req.FinancialAccountId?.Value);
        }

        payable.ApplyAutoApproval(cc.AutoApprovalLimit, clock.GetUtcNow());

        await uow.AddAsync(payable, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/payables/{new PayableId(payable.Id)}", PayableResponse.From(payable));
    }
}
