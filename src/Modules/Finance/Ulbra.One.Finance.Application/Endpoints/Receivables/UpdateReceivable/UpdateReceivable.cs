using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Results;
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
        [SwaggerSchema("Customer who owes it. The company is immutable.")]
        public CustomerId PayerId { get; set; }

        [SwaggerSchema("Cost center the revenue is credited to.")]
        public CostCenterId CostCenterId { get; set; }

        [SwaggerSchema("Revenue category. Optional.")]
        public Guid? RevenueCategoryId { get; set; }

        [SwaggerSchema("Revenue ledger account for the accounting posting. Optional.")]
        public Guid? LedgerAccountId { get; set; }

        [SwaggerSchema("What is being charged for.")]
        [SwaggerExample("Mensalidade do convenio - agosto/2026")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Number of the document backing the charge.")]
        [SwaggerExample("NF 12345")]
        [NormalizeText] public string? DocumentNumber { get; set; }

        [SwaggerSchema("Issue date of the document - the accounting competency anchor.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly? IssueDate { get; set; }

        [SwaggerSchema("The complete installment schedule. It REPLACES the current one, so any installment "
            + "left out is dropped.")]
        public List<CRCreateReceivable.ReceivableInstallmentInput>? Installments { get; set; }

        [SwaggerSchema("Rebuild the schedule from a total, a count and a first due date.")]
        public CRCreateReceivable.ReceivableGenerateInput? Generate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateReceivable").WithTags("Finance")
            .WithSummary("Updates a receivable.")
            .WithDescription("Replaces the receivable\u0027s editable fields and its whole installment schedule. A full "
                + "replace, not a patch. A receivable that already has a receipt registered cannot be edited "
                + "(409) - reverse the receipt first. The owning company is immutable.")
            .RequirePermission(Permissions.ReceivablesWrite).WithRequestNormalizer<UpdateReceivableRequest>()
            .Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, UpdateReceivableRequest req,
        IUnitOfWork uow, TimeProvider timeProvider, CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound(ErrorCodes.Receivable.NotFound, "Receivable not found.")).ToOk();

        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == receivable.CompanyId, ct);
        if (costCenter is null)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CostCenterInvalid, "Cost center is invalid for this company.")).ToOk();
        var customer = await uow.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == receivable.CompanyId, ct);
        if (customer is null)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CustomerInvalid, "Customer is invalid for this company.")).ToOk();
        if (!customer.Active)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CustomerInactive, "Customer is inactive.")).ToOk();

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
            return ErrorResults.InvalidState(ex.Message, ErrorCodes.Receivable.NotEditable);
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
