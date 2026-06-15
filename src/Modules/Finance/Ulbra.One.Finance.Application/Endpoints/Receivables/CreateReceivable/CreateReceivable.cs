using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Normalization;
using Ulbra.One.Common.Infrastructure.Results;
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
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;

        var costCenter = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == companyId, ct);
        if (costCenter is null)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CostCenterInvalid", "Cost center is invalid for this company.")).ToOk();

        var customer = await registry.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == companyId, ct);
        if (customer is null)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CustomerInvalid", "Customer is invalid for this company.")).ToOk();
        if (!customer.Active)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CustomerInactive", "Customer is inactive.")).ToOk();

        List<ReceivableInstallment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.InstallmentsInvalid", "Invalid installment generation.")).ToOk();
            installments = Receivable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.InstallmentsInvalid", "Each installment amount must be greater than zero.")).ToOk();
            installments = rows.Select((r, i) => new ReceivableInstallment(i + 1, r.DueDate, r.Amount)).ToList();
        }
        else return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.InstallmentsInvalid", "Provide either explicit installments or a generation spec.")).ToOk();

        var receivable = new Receivable(companyId, req.Origin, customer.Id, customer.Name, req.DocumentNumber, req.IssueDate,
            req.Description, costCenter.Id, req.RevenueCategoryId, req.LedgerAccountId, installments);

        await uow.AddAsync(receivable, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{new ReceivableId(receivable.Id)}",
            ReceivableResponse.From(receivable, ReceivableClock.Today(timeProvider)));
    }
}
