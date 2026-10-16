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
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.CreateTransaction;

public sealed class CreateTransaction : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company the transaction belongs to.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Unit the transaction is charged to.")]
        public UnitId UnitId { get; set; }

        [SwaggerSchema("Cost center the transaction is charged to.")]
        public CostCenterId CostCenterId { get; set; }

        [SwaggerSchema("Whether this is money going out or coming in — it decides which counterparty "
            + "field applies.")]
        public TransactionType Type { get; set; }

        [SwaggerSchema("What the transaction is for.")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Vendor, for an outgoing transaction.")]
        public VendorId? VendorId { get; set; }

        [SwaggerSchema("Customer, for an incoming transaction.")]
        public CustomerId? CustomerId { get; set; }

        [SwaggerSchema("Amount in BRL. Settled in full — this entity has no installments.")]
        [SwaggerExample("1250.00")]
        public decimal Amount { get; set; }

        [SwaggerSchema("When it falls due.")]
        [SwaggerExample("2026-09-10")]
        public DateOnly DueDate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateTransaction")
            .WithSummary("Creates a legacy transaction.")
            .WithDescription("LEGACY, superseded by the Payable aggregate - use POST /api/payables instead. This entity is "
                + "not extended any further.")
            .WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite)
            .WithRequestNormalizer<Request>()
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;
        var unitId = req.UnitId.Value;
        var costCenterId = req.CostCenterId.Value;

        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == costCenterId && c.CompanyId == companyId, ct);
        if (cc is null) return ErrorResults.InvalidRequest("Cost center is invalid for this company.", ErrorCodes.Transaction.CostCenterInvalid);

        var unitOk = await uow.ReadSet<Unit>().AnyAsync(u => u.Id == unitId && u.CompanyId == companyId, ct);
        if (!unitOk) return ErrorResults.InvalidRequest("Unit is invalid for this company.", ErrorCodes.Transaction.UnitInvalid);

        if (req.Amount <= 0) return ErrorResults.InvalidRequest("Amount must be greater than zero.", ErrorCodes.Transaction.AmountInvalid);

        Guid? vendorId = null;
        Guid? customerId = null;
        string counterpartyName;

        if (req.Type == TransactionType.Payable)
        {
            if (req.VendorId is not { } vId)
                return ErrorResults.InvalidRequest("A vendor is required for a payable.", ErrorCodes.Transaction.VendorRequired);
            var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == vId.Value, ct);
            if (vendor is null)
                return ErrorResults.InvalidRequest("Vendor not found.", ErrorCodes.Vendor.NotFound);
            vendorId = vendor.Id;
            counterpartyName = vendor.Name;
        }
        else
        {
            if (req.CustomerId is not { } cId)
                return ErrorResults.InvalidRequest("A customer is required for a receivable.", ErrorCodes.Transaction.CustomerRequired);
            var customer = await uow.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == cId.Value && c.CompanyId == companyId, ct);
            if (customer is null)
                return ErrorResults.InvalidRequest("Customer is invalid for this company.", ErrorCodes.Transaction.CustomerInvalid);
            customerId = customer.Id;
            counterpartyName = customer.Name;
        }

        var t = new Transaction(companyId, unitId, costCenterId, req.Type,
            req.Description, vendorId, customerId, counterpartyName, req.Amount, req.DueDate);
        t.ApplyAutoApproval(cc.AutoApprovalLimit);

        await uow.AddAsync(t, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/transactions/{new TransactionId(t.Id)}", TransactionResponse.From(t));
    }
}
