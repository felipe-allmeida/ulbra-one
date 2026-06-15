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
        [FromKeyedServices("registry")] IUnitOfWork registry, TimeProvider timeProvider, CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound("Receivable.NotFound", "Receivable not found.")).ToOk();

        var costCenter = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == receivable.CompanyId, ct);
        if (costCenter is null)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CostCenterInvalid", "Cost center is invalid for this company.")).ToOk();
        var customer = await registry.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == receivable.CompanyId, ct);
        if (customer is null)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CustomerInvalid", "Customer is invalid for this company.")).ToOk();
        if (!customer.Active)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.CustomerInactive", "Customer is inactive.")).ToOk();

        var installmentsResult = ReceivableInstallmentsFactory.Build(req.Generate, req.Installments);
        if (installmentsResult.IsFailure)
            return Result.Failure<ReceivableResponse>(installmentsResult.Error).ToOk();

        var oldInstallments = receivable.Installments.ToList();
        try
        {
            receivable.Update(customer.Id, customer.Name, req.DocumentNumber, req.IssueDate, req.Description, costCenter.Id, req.RevenueCategoryId, req.LedgerAccountId, installmentsResult.Value);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receivable.NotEditable");
        }
        // EF does not auto-track changes to the private List<T> backing field, so we manage the
        // installment set explicitly: delete old rows and register new ones.
        uow.RemoveRange(oldInstallments);
        foreach (var inst in receivable.Installments)
            await uow.AddAsync(inst, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ReceivableResponse.From(receivable, ReceivableClock.Today(timeProvider)));
    }
}
