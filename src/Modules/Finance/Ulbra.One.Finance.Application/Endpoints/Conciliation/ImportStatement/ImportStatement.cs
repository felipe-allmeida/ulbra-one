using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        ILogger<ImportStatement> logger,
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OFX parse failed for {FileName} (bank account {BankAccountId})", file.FileName, bankAccountId.Value);
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
            StatementImportResponse.From(import, AccountMismatchWarning(account, parsed.Account)));
    }

    /// <summary>
    /// Builds a pt-BR warning when the imported statement's <c>BANKACCTFROM</c> looks like it belongs to a
    /// different account than the one selected. Non-blocking (warn-and-allow): returns null when it matches,
    /// when the OFX carries no account block, or when neither side has comparable data.
    /// </summary>
    private static string? AccountMismatchWarning(BankAccount account, ParsedAccount? parsed)
    {
        if (parsed is null) return null;

        var detectedBank = DigitsOnly(parsed.BankId).TrimStart('0');
        var selectedBank = DigitsOnly(account.BankCode).TrimStart('0');
        var bankMismatch = detectedBank.Length > 0 && selectedBank.Length > 0 && detectedBank != selectedBank;

        var detectedAccount = DigitsOnly(parsed.AccountId);
        var selectedAccount = string.IsNullOrWhiteSpace(account.OfxAccountId)
            ? DigitsOnly(account.Agency + account.AccountNumber + account.AccountDigit)
            : DigitsOnly(account.OfxAccountId);
        var accountMismatch = detectedAccount.Length > 0 && selectedAccount.Length > 0
            && detectedAccount != selectedAccount
            && !detectedAccount.Contains(selectedAccount, StringComparison.Ordinal)
            && !selectedAccount.Contains(detectedAccount, StringComparison.Ordinal);

        if (!bankMismatch && !accountMismatch) return null;

        var detected = $"{Present(parsed.BankId)}/{Present(parsed.AccountId)}".Trim('/');
        var selected = $"{account.BankCode}/{account.Agency}/{account.AccountNumber}-{account.AccountDigit}";
        return $"O extrato importado parece pertencer à conta {detected}, mas a conta selecionada é {selected}. Verifique se selecionou a conta correta.";
    }

    private static string DigitsOnly(string? s) => new((s ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string Present(string? s) => string.IsNullOrWhiteSpace(s) ? "?" : s.Trim();

    private static async Task RunMatcher(IUnitOfWork uow, BankAccount account, List<BankTransaction> newTxns, CancellationToken ct)
    {
        // Deterministic order so the greedy one-to-one assignment below is stable across imports.
        var debits = newTxns
            .Where(t => t.Direction == TransactionDirection.Debit)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Amount)
            .ThenBy(t => t.ExternalId, StringComparer.Ordinal)
            .ToList();
        if (debits.Count == 0) return;

        var candidates = await (
            from p in uow.ReadSet<Payable>()
            where p.CompanyId == account.CompanyId && (p.Status == PayableStatus.Scheduled || p.Status == PayableStatus.Paid)
            from i in p.Installments
            where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
            select new InstallmentCandidate(i.Id, i.NetAmount - i.PaidAmount, i.DueDate))
            .ToListAsync(ct);

        // Greedy one-to-one: a suggested installment is removed from the pool so two same-value debits
        // (e.g. the many identical "PAGFOR PIX" lines in a Santander file) never both claim the same parcela.
        // Only unique, exact-value matches within the date window are suggested (see ConciliationMatcher).
        var pool = candidates.ToList();
        foreach (var t in debits)
        {
            if (ConciliationMatcher.FindSuggestion(t.Amount, t.Date, pool) is { } id)
            {
                t.Suggest(id);
                pool.RemoveAll(c => c.InstallmentId == id);
            }
        }
    }
}
