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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.CreateReceivable;

public sealed class CreateReceivable : IEndpoint
{
    public sealed record ReceivableInstallmentInput(DateOnly DueDate, decimal Amount);
    public sealed record ReceivableGenerateInput(decimal TotalAmount, int Count, DateOnly FirstDueDate, int IntervalMonths = 1);

    public sealed record CreateReceivableRequest
    {
        public CompanyId CompanyId { get; set; }
        public CustomerId PayerId { get; set; }
        public CostCenterId CostCenterId { get; set; }
        public Guid? RevenueCategoryId { get; set; }
        public Guid? LedgerAccountId { get; set; }
        public ReceivableOrigin Origin { get; set; } = ReceivableOrigin.Manual;
        [NormalizeText] public string Description { get; set; } = string.Empty;
        [NormalizeText] public string? DocumentNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public List<ReceivableInstallmentInput>? Installments { get; set; }
        public ReceivableGenerateInput? Generate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateReceivable").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<CreateReceivableRequest>()
            .Produces<ReceivableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        CreateReceivableRequest req,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        [FromKeyedServices("core")] IUnitOfWork core,
        [FromKeyedServices("registry")] IUnitOfWork registry,
        TimeProvider clock,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;

        var cc = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == companyId, ct);
        if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.", statusCode: 400, title: "Receivable.CostCenterInvalid");

        var customer = await registry.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == companyId, ct);
        if (customer is null) return Results.Problem(detail: "Customer is invalid for this company.", statusCode: 400, title: "Receivable.CustomerInvalid");
        if (!customer.Active) return Results.Problem(detail: "Customer is inactive.", statusCode: 400, title: "Receivable.CustomerInactive");

        List<ReceivableInstallment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return Results.Problem(detail: "Invalid installment generation.", statusCode: 400, title: "Receivable.InstallmentsInvalid");
            installments = Receivable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return Results.Problem(detail: "Each installment amount must be greater than zero.", statusCode: 400, title: "Receivable.InstallmentsInvalid");
            installments = rows.Select((r, i) => new ReceivableInstallment(i + 1, r.DueDate, r.Amount)).ToList();
        }
        else
        {
            return Results.Problem(detail: "Provide either explicit installments or a generation spec.", statusCode: 400, title: "Receivable.InstallmentsInvalid");
        }

        var receivable = new Receivable(companyId, req.Origin, customer.Id, customer.Name, req.DocumentNumber, req.IssueDate,
            req.Description, cc.Id, req.RevenueCategoryId, req.LedgerAccountId, installments);

        await uow.AddAsync(receivable, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{new ReceivableId(receivable.Id)}",
            ReceivableResponse.From(receivable, ReceivableClock.Today(clock)));
    }
}
