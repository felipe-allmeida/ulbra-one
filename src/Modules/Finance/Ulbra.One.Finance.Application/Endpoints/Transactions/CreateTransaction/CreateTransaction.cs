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
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.CreateTransaction;

public sealed class CreateTransaction : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        public UnitId UnitId { get; set; }
        public CostCenterId CostCenterId { get; set; }
        public TransactionType Type { get; set; }
        [NormalizeText] public string Description { get; set; } = string.Empty;
        public VendorId? VendorId { get; set; }
        public CustomerId? CustomerId { get; set; }
        public decimal Amount { get; set; }
        public DateOnly DueDate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateTransaction")
            .WithTags("Finance")
            .RequireAuthorization()
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
        if (cc is null) return TypedResults.Problem(detail: "Cost center is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.CostCenterInvalid");

        var unitOk = await uow.ReadSet<Unit>().AnyAsync(u => u.Id == unitId && u.CompanyId == companyId, ct);
        if (!unitOk) return TypedResults.Problem(detail: "Unit is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.UnitInvalid");

        if (req.Amount <= 0) return TypedResults.Problem(detail: "Amount must be greater than zero.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.AmountInvalid");

        Guid? vendorId = null;
        Guid? customerId = null;
        string counterpartyName;

        if (req.Type == TransactionType.Payable)
        {
            if (req.VendorId is not { } vId)
                return TypedResults.Problem(detail: "A vendor is required for a payable.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.VendorRequired");
            var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == vId.Value && v.CompanyId == companyId, ct);
            if (vendor is null)
                return TypedResults.Problem(detail: "Vendor is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.VendorInvalid");
            vendorId = vendor.Id;
            counterpartyName = vendor.Name;
        }
        else
        {
            if (req.CustomerId is not { } cId)
                return TypedResults.Problem(detail: "A customer is required for a receivable.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.CustomerRequired");
            var customer = await uow.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == cId.Value && c.CompanyId == companyId, ct);
            if (customer is null)
                return TypedResults.Problem(detail: "Customer is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.CustomerInvalid");
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
