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

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.CreateLedgerAccount;

public sealed class CreateLedgerAccount : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Hierarchical account code. Unique across the chart and what the chart is ordered by.")]
        [SwaggerExample("3.1.01.001")]
        [NormalizeText] public string Code { get; set; } = string.Empty;

        /// <summary>Optional short numeric alias ("código reduzido"); unique when present.</summary>
        [SwaggerSchema("Short numeric alias (código reduzido) used for fast entry. Optional, and unique "
            + "when supplied.")]
        [SwaggerExample("31101")]
        [NormalizeText] public string? ReducedCode { get; set; }

        /// <summary>Optional "Natureza" classification (1–5) from the chart.</summary>
        [SwaggerSchema("Natureza classification from the chart, 1 to 5. Optional.")]
        [SwaggerExample("4")]
        public int? Natureza { get; set; }

        [SwaggerSchema("Account name as it appears in the chart and in the reports.")]
        [SwaggerExample("Manutenção e conservação predial")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Which statement group the account belongs to — it decides where the account "
            + "surfaces in the balance sheet or income statement.")]
        public AccountGroup AccountGroup { get; set; }

        [SwaggerSchema("Whether the account receives entries or only groups others. A synthetic "
            + "(grouping) account cannot be used on a journal line.")]
        public LedgerAccountKind Kind { get; set; }

        [SwaggerSchema("Parent account in the chart hierarchy. Omit for a root account.")]
        public LedgerAccountId? ParentId { get; set; }

        /// <summary>Optional override; defaults to the natural balance for the group when omitted.</summary>
        [SwaggerSchema("Which side is the account's normal balance. Omit to take the natural balance of "
            + "its group — override only for the rare account that behaves against its group.")]
        public NormalBalance? NormalBalance { get; set; }

        /// <summary>Optional DRE cascade line for result accounts (null on non-result accounts).</summary>
        [SwaggerSchema("Which line of the income-statement cascade this account rolls up into. Only "
            + "meaningful for result accounts; leave null otherwise.")]
        public DreLine? DreLine { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateLedgerAccount").WithTags("Accounting")
            .WithSummary("Creates a ledger account.")
            .WithDescription("Adds an account to the chart of accounts. The chart is global — one canonical "
                + "plano de contas shared by every company — so an account created here is visible to all "
                + "of them. Code and reduced code must be unique; a collision returns 409.")
            .RequirePermission(Permissions.AccountingWrite).WithRequestNormalizer<Request>()
            .Produces<LedgerAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return ErrorResults.InvalidRequest("Code is required.", ErrorCodes.LedgerAccount.Invalid);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.LedgerAccount.Invalid);

        LedgerAccount account;
        try
        {
            account = new LedgerAccount(req.Code, req.Name, req.AccountGroup, req.Kind,
                parentId: null, req.NormalBalance, req.ReducedCode, req.Natureza, req.DreLine);
        }
        catch (ArgumentException ex)
        {
            return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.LedgerAccount.Invalid);
        }

        if (req.ParentId is { } parentId)
        {
            var parent = await uow.Set<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == parentId.Value, ct);
            if (parent is null)
                return ErrorResults.InvalidRequest("Parent account not found.", ErrorCodes.LedgerAccount.InvalidParent);
            try { account.Reparent(parent); }
            catch (InvalidOperationException ex) { return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.LedgerAccount.InvalidParent); }
        }

        var duplicate = await uow.ReadSet<LedgerAccount>()
            .AnyAsync(a => a.Code == account.Code && a.DeletedAt == null, ct);
        if (duplicate)
            return ErrorResults.InvalidState("A ledger account with this code already exists.", ErrorCodes.LedgerAccount.DuplicateCode);

        await uow.AddAsync(account, ct);
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return ErrorResults.InvalidState("A ledger account with this code already exists.", ErrorCodes.LedgerAccount.DuplicateCode);
        }

        return TypedResults.Created($"/api/ledger-accounts/{new LedgerAccountId(account.Id)}", LedgerAccountResponse.From(account));
    }
}
