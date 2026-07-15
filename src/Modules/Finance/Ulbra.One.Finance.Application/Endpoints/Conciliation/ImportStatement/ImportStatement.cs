using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ImportStatement;

public sealed class ImportStatement : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("ImportStatement").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite).DisableAntiforgery()
            .Produces<StatementImportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        BankAccountId bankAccountId,
        IFormFile file,
        IBankStatementParser parser,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var account = await uow.ReadSet<BankAccount>().FirstOrDefaultAsync(b => b.Id == bankAccountId.Value, ct);
        if (account is null)
            return TypedResults.Problem(detail: "Bank account not found.", statusCode: 400, title: "BankAccount.NotFound");

        if (file.Length == 0)
            return TypedResults.Problem(detail: "File is empty.", statusCode: 400, title: "File.Empty");

        ParsedStatement parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = await parser.ParseAsync(stream, ct);
        }
        catch
        {
            return TypedResults.Problem(detail: "Could not parse the bank statement.", statusCode: 400, title: "Statement.ParseFailed");
        }

        var existingIds = await uow.ReadSet<BankTransaction>()
            .Where(t => t.BankAccountId == account.Id)
            .Select(t => t.ExternalId)
            .ToListAsync(ct);
        var seen = new HashSet<string>(existingIds, StringComparer.Ordinal);

        var toIngest = new List<ParsedTransaction>();
        var duplicateCount = 0;
        foreach (var p in parsed.Transactions)
        {
            if (string.IsNullOrWhiteSpace(p.ExternalId)) continue;
            if (!seen.Add(p.ExternalId)) { duplicateCount++; continue; }
            toIngest.Add(p);
        }

        var import = new BankStatementImport(
            account.CompanyId, account.Id, parser.Source, file.FileName,
            parsed.PeriodStart, parsed.PeriodEnd, parsed.Transactions.Count, toIngest.Count, duplicateCount);

        var now = clock.GetUtcNow();
        var newTxns = toIngest
            .Select(p => new BankTransaction(import.Id, account.CompanyId, account.Id, p.ExternalId, p.Date, p.Amount, p.Description, now))
            .ToList();

        await RunMatcher(uow, account, newTxns, ct);

        await uow.AddAsync(import, ct);
        foreach (var t in newTxns)
            await uow.AddAsync(t, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/statement-imports/{new StatementImportId(import.Id)}",
            StatementImportResponse.From(import));
    }

    private static async Task RunMatcher(IUnitOfWork uow, BankAccount account, List<BankTransaction> newTxns, CancellationToken ct)
    {
        if (!newTxns.Any(t => t.Direction == TransactionDirection.Debit)) return;

        var candidates = await (
            from p in uow.ReadSet<Payable>()
            where p.CompanyId == account.CompanyId && (p.Status == PayableStatus.Scheduled || p.Status == PayableStatus.Paid)
            from i in p.Installments
            where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
            select new InstallmentCandidate(i.Id, i.NetAmount - i.PaidAmount, i.DueDate))
            .ToListAsync(ct);

        foreach (var t in newTxns.Where(t => t.Direction == TransactionDirection.Debit))
            if (ConciliationMatcher.FindSuggestion(t.Amount, t.Date, candidates) is { } id)
                t.Suggest(id);
    }
}
