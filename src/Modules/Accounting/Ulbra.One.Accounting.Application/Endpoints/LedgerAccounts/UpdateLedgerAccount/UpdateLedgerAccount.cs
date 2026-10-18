using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.UpdateLedgerAccount;

public sealed class UpdateLedgerAccount : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Account name as it appears in the chart and in the reports.")]
        [SwaggerExample("Manutenção e conservação predial")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        /// <summary>Optional short numeric alias ("código reduzido"); unique when present.</summary>
        [SwaggerSchema("Short numeric alias (código reduzido). Optional, and unique when supplied.")]
        [SwaggerExample("31101")]
        [NormalizeText] public string? ReducedCode { get; set; }

        /// <summary>Optional "Natureza" classification (1–5) from the chart.</summary>
        [SwaggerSchema("Natureza classification from the chart, 1 to 5. Optional.")]
        [SwaggerExample("4")]
        public int? Natureza { get; set; }

        [SwaggerSchema("Which statement group the account belongs to.")]
        public AccountGroup AccountGroup { get; set; }

        [SwaggerSchema("Whether the account receives entries or only groups others.")]
        public LedgerAccountKind Kind { get; set; }

        [SwaggerSchema("Which side is the account's normal balance. Required here, unlike on creation, "
            + "where it can be derived from the group.")]
        public NormalBalance NormalBalance { get; set; }

        [SwaggerSchema("Parent account in the chart hierarchy. Omit for a root account.")]
        public LedgerAccountId? ParentId { get; set; }

        [SwaggerSchema("Whether the account can still receive new entries. Deactivating keeps it in the "
            + "chart and keeps its history in the reports — accounts are never deleted.")]
        public bool IsActive { get; set; } = true;

        /// <summary>Optional DRE cascade line for result accounts (null on non-result accounts).</summary>
        [SwaggerSchema("Which line of the income-statement cascade this account rolls up into. Only "
            + "meaningful for result accounts.")]
        public DreLine? DreLine { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateLedgerAccount").WithTags("Accounting")
            .WithSummary("Updates a ledger account.")
            .WithDescription("Replaces the account's editable fields. A full replace, not a patch. The "
                + "code is immutable — renaming an account is fine, renumbering it is not, because posted "
                + "entries already reference it. There is no delete: set IsActive to false instead.")
            .RequirePermission(Permissions.AccountingWrite).WithRequestNormalizer<Request>()
            .Produces<LedgerAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(LedgerAccountId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var account = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null) return ErrorResults.NotFound("Ledger account not found.", ErrorCodes.LedgerAccount.NotFound);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.LedgerAccount.Invalid);

        try { account.Update(req.Name, req.AccountGroup, req.Kind, req.NormalBalance, req.ReducedCode, req.Natureza, req.DreLine); }
        catch (ArgumentException ex) { return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.LedgerAccount.Invalid); }

        if (req.ParentId is { } parentId)
        {
            var parent = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return ErrorResults.InvalidRequest("Parent account not found.", ErrorCodes.LedgerAccount.InvalidParent);
            try { account.Reparent(parent); }
            catch (InvalidOperationException ex) { return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.LedgerAccount.InvalidParent); }
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
