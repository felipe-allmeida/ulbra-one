using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.CreateLedgerAccount;

public sealed class CreateLedgerAccount : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        [NormalizeText] public string Code { get; set; } = string.Empty;
        [NormalizeText] public string Name { get; set; } = string.Empty;
        public AccountGroup AccountGroup { get; set; }
        public LedgerAccountKind Kind { get; set; }
        public LedgerAccountId? ParentId { get; set; }
        /// <summary>Optional override; defaults to the natural balance for the group when omitted.</summary>
        public NormalBalance? NormalBalance { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateLedgerAccount").WithTags("Accounting")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<LedgerAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return TypedResults.Problem(detail: "Code is required.", statusCode: 400, title: "LedgerAccount.Invalid");
        if (string.IsNullOrWhiteSpace(req.Name))
            return TypedResults.Problem(detail: "Name is required.", statusCode: 400, title: "LedgerAccount.Invalid");

        var companyId = req.CompanyId.Value;
        LedgerAccount account;
        try
        {
            account = new LedgerAccount(companyId, req.Code, req.Name, req.AccountGroup, req.Kind,
                parentId: null, req.NormalBalance);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "LedgerAccount.Invalid");
        }

        if (req.ParentId is { } parentId)
        {
            var parent = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return TypedResults.Problem(detail: "Parent account not found.", statusCode: 400, title: "LedgerAccount.InvalidParent");
            try { account.Reparent(parent); }
            catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "LedgerAccount.InvalidParent"); }
        }

        var duplicate = await uow.ReadSet<LedgerAccount>()
            .AnyAsync(a => a.CompanyId == companyId && a.Code == account.Code, ct);
        if (duplicate)
            return TypedResults.Problem(detail: "A ledger account with this code already exists for the company.", statusCode: 409, title: "LedgerAccount.DuplicateCode");

        await uow.AddAsync(account, ct);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(detail: "A ledger account with this code already exists for the company.", statusCode: 409, title: "LedgerAccount.DuplicateCode");
        }

        return TypedResults.Created($"/api/ledger-accounts/{new LedgerAccountId(account.Id)}", LedgerAccountResponse.From(account));
    }
}
