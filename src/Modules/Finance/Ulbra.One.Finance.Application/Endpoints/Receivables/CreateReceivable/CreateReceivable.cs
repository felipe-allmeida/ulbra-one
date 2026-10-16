using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
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
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.CreateReceivable;

public sealed class CreateReceivable : IEndpoint
{
    public sealed record ReceivableInstallmentInput(
        [property: SwaggerSchema("When this installment falls due.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly DueDate,
        [property: SwaggerSchema("Installment amount in BRL. The title total is the sum of its installments.")]
        [property: SwaggerExample("1250.00")]
        decimal Amount);

    public sealed record ReceivableGenerateInput(
        [property: SwaggerSchema("Total to split across the generated installments, in BRL.")]
        [property: SwaggerExample("12000.00")]
        decimal TotalAmount,
        [property: SwaggerSchema("How many installments to generate.")]
        [property: SwaggerExample("12")]
        int Count,
        [property: SwaggerSchema("Due date of the first installment; the rest follow at IntervalMonths spacing.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly FirstDueDate,
        [property: SwaggerSchema("Months between installments. 1 gives a monthly schedule.")]
        [property: SwaggerExample("1")]
        int IntervalMonths = 1);

    public sealed record CreateReceivableRequest
    {
        [SwaggerSchema("Company that is owed.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Customer who owes it.")]
        public CustomerId PayerId { get; set; }

        [SwaggerSchema("Cost center the revenue is credited to.")]
        public CostCenterId CostCenterId { get; set; }

        [SwaggerSchema("Revenue category. Optional.")]
        public Guid? RevenueCategoryId { get; set; }

        [SwaggerSchema("Revenue ledger account for the accounting posting. Optional.")]
        public Guid? LedgerAccountId { get; set; }

        [SwaggerSchema("Where the receivable came from. Leave the default for one typed in by hand.")]
        public ReceivableOrigin Origin { get; set; } = ReceivableOrigin.Manual;

        [SwaggerSchema("What is being charged for.")]
        [SwaggerExample("Mensalidade do convenio - agosto/2026")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Number of the document backing the charge.")]
        [SwaggerExample("NF 12345")]
        [NormalizeText] public string? DocumentNumber { get; set; }

        [SwaggerSchema("Issue date of the document - the accounting competency anchor.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly? IssueDate { get; set; }

        [SwaggerSchema("The installment schedule, authored one by one. Send either this or Generate.")]
        public List<ReceivableInstallmentInput>? Installments { get; set; }

        [SwaggerSchema("Ask the server to build the schedule from a total, a count and a first due date.")]
        public ReceivableGenerateInput? Generate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateReceivable").WithTags("Finance")
            .WithSummary("Creates a receivable.")
            .WithDescription("Registers an account receivable with its installment schedule. Give the schedule one of two "
                + "ways: Installments to author each one, or Generate to split a total across N installments.")
            .RequirePermission(Permissions.ReceivablesWrite).WithRequestNormalizer<CreateReceivableRequest>()
            .Produces<ReceivableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        CreateReceivableRequest req,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;

        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == companyId, ct);
        if (costCenter is null)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CostCenterInvalid, "Cost center is invalid for this company.")).ToOk();

        var customer = await uow.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == req.PayerId.Value && c.CompanyId == companyId, ct);
        if (customer is null)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CustomerInvalid, "Customer is invalid for this company.")).ToOk();
        if (!customer.Active)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.CustomerInactive, "Customer is inactive.")).ToOk();

        var installmentsResult = ReceivableInstallmentsFactory.Build(req.Generate, req.Installments);
        if (installmentsResult.IsFailure)
            return Result.Failure<ReceivableResponse>(installmentsResult.Error).ToOk();

        var receivable = new Receivable(companyId, req.Origin, customer.Id, customer.Name, req.DocumentNumber, req.IssueDate,
            req.Description, costCenter.Id, req.RevenueCategoryId, req.LedgerAccountId, installmentsResult.Value);

        await uow.AddAsync(receivable, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{new ReceivableId(receivable.Id)}",
            ReceivableResponse.From(receivable, ReceivableClock.Today(timeProvider)));
    }
}
