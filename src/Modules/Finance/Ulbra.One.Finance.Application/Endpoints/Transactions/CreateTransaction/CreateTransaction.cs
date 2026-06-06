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
        [NormalizeText] public string Counterparty { get; set; } = string.Empty;
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
        [FromKeyedServices("finance")] IUnitOfWork uow,
        [FromKeyedServices("core")] IUnitOfWork core,
        CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;
        var unitId = req.UnitId.Value;
        var costCenterId = req.CostCenterId.Value;

        var cc = await core.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == costCenterId && c.CompanyId == companyId, ct);
        if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.CostCenterInvalid");

        var unitOk = await core.ReadSet<Unit>().AnyAsync(u => u.Id == unitId && u.CompanyId == companyId, ct);
        if (!unitOk) return Results.Problem(detail: "Unit is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.UnitInvalid");

        if (req.Amount <= 0) return Results.Problem(detail: "Amount must be greater than zero.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.AmountInvalid");

        var t = new Transaction(companyId, unitId, costCenterId, req.Type,
            req.Description, req.Counterparty, req.Amount, req.DueDate);
        t.ApplyAutoApproval(cc.AutoApprovalLimit);

        await uow.AddAsync(t, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/transactions/{new TransactionId(t.Id)}", TransactionResponse.From(t));
    }
}
