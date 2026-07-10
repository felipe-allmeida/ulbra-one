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

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.UpdateLedgerAccount;

public sealed class UpdateLedgerAccount : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string Name { get; set; } = string.Empty;
        /// <summary>Optional short numeric alias ("código reduzido"); unique when present.</summary>
        [NormalizeText] public string? ReducedCode { get; set; }
        /// <summary>Optional "Natureza" classification (1–5) from the chart.</summary>
        public int? Natureza { get; set; }
        public AccountGroup AccountGroup { get; set; }
        public LedgerAccountKind Kind { get; set; }
        public NormalBalance NormalBalance { get; set; }
        public LedgerAccountId? ParentId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateLedgerAccount").WithTags("Accounting")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<LedgerAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(LedgerAccountId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var account = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null) return TypedResults.Problem(detail: "Ledger account not found.", statusCode: 404, title: "LedgerAccount.NotFound");
        if (string.IsNullOrWhiteSpace(req.Name))
            return TypedResults.Problem(detail: "Name is required.", statusCode: 400, title: "LedgerAccount.Invalid");

        try { account.Update(req.Name, req.AccountGroup, req.Kind, req.NormalBalance, req.ReducedCode, req.Natureza); }
        catch (ArgumentException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "LedgerAccount.Invalid"); }

        if (req.ParentId is { } parentId)
        {
            var parent = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return TypedResults.Problem(detail: "Parent account not found.", statusCode: 400, title: "LedgerAccount.InvalidParent");
            try { account.Reparent(parent); }
            catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "LedgerAccount.InvalidParent"); }
        }
        else
        {
            account.Reparent(null);
        }

        if (req.IsActive) account.Activate(); else account.Deactivate();

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(LedgerAccountResponse.From(account));
    }
}
