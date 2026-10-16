using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ImportStatement;

public sealed class ImportStatement : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("ImportStatement").WithTags("Finance")
            .WithSummary("Imports a bank statement.")
            .WithDescription("Uploads a bank statement file (multipart/form-data) and turns each line into a bank "
                + "transaction ready to reconcile. The parser detects the format. Import is the entry point of "
                + "the whole reconciliation flow: matching, confirming and ignoring all act on the transactions "
                + "created here.")
            .RequirePermission(Permissions.TreasuryWrite).DisableAntiforgery()
            .Produces<StatementImportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Bank account the statement belongs to. Required — every imported line is bound "
            + "to it.")]
        BankAccountId bankAccountId,
        [SwaggerSchema("The statement file. The parser detects the format from its contents.")]
        IFormFile file,
        IBankStatementParser parser,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<ImportStatement> logger,
        CancellationToken ct)
    {
        var account = await uow.ReadSet<BankAccount>().FirstOrDefaultAsync(b => b.Id == bankAccountId.Value, ct);
        if (account is null)
            return ErrorResults.InvalidRequest("Bank account not found.", ErrorCodes.BankAccount.NotFound);

        if (file.Length == 0)
            return ErrorResults.InvalidRequest("File is empty.", ErrorCodes.File.Empty);

        ParsedStatement parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = await parser.ParseAsync(stream, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OFX parse failed for {FileName} (bank account {BankAccountId})", file.FileName, bankAccountId.Value);
            return ErrorResults.InvalidRequest("Could not parse the bank statement.", ErrorCodes.Statement.ParseFailed);
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

        // No early return on an empty batch: re-importing a file is deduped by FITID and brings in nothing new,
        // and that is precisely the case where the second pass has work to do over what earlier imports left
        // unmatched. Only the open-installment pass below is skipped.
        if (debits.Count > 0)
            await SuggestOpenInstallmentsAsync(uow, account, debits, ct);

        await LinkAlreadySettledAsync(uow, account, debits, ct);
    }

    /// <summary>First pass: a debit that matches a single OPEN installment becomes a suggestion for the operator
    /// to confirm — confirming is what registers the baixa.</summary>
    private static async Task SuggestOpenInstallmentsAsync(
        IUnitOfWork uow, BankAccount account, List<BankTransaction> debits, CancellationToken ct)
    {
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

    /// <summary>
    /// Every debit on this account still waiting to be matched: the ones arriving in this import plus the ones
    /// already persisted from earlier imports. The second pass has to see both — a statement imported BEFORE the
    /// linking existed would otherwise stay orphaned forever, since re-importing the same file is deduped by
    /// FITID and reaches the matcher with an empty list.
    /// </summary>
    private static async Task<List<BankTransaction>> UnmatchedDebitsAsync(
        IUnitOfWork uow, BankAccount account, List<BankTransaction> incoming, CancellationToken ct)
    {
        var persisted = await uow.Set<BankTransaction>()
            .Where(t => t.BankAccountId == account.Id
                        && t.MatchStatus == MatchStatus.Unmatched
                        && t.Amount < 0)
            .ToListAsync(ct);

        // The incoming rows are not in the DB yet (they are added after the matcher runs), so they are unioned
        // in by identity rather than re-queried.
        return persisted
            .Concat(incoming.Where(t => t.MatchStatus == MatchStatus.Unmatched))
            .DistinctBy(t => t.Id)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Amount)
            .ThenBy(t => t.ExternalId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Second pass, over the debits the open-installment matcher could not place. A título paid through the CNAB
    /// return leaves its installment settled, so it never enters that pool — its statement debit would otherwise
    /// read as "não conciliado" forever, even though the payment is right there in the system.
    /// <para>
    /// This links the two without registering anything: the money already moved and the baixa already exists.
    /// Greedy one-to-one on the payment side, so two identical debits never claim the same payment, and the
    /// linked transactions are flagged <c>PaymentPreexisting</c> so undoing the link never reverses a real payment.
    /// </para>
    /// </summary>
    private static async Task LinkAlreadySettledAsync(
        IUnitOfWork uow, BankAccount account, List<BankTransaction> debits, CancellationToken ct)
    {
        // Not just this import's debits: every unmatched debit on the account, so an import sweeps whatever
        // earlier ones left behind (see UnmatchedDebitsAsync).
        var pending = await UnmatchedDebitsAsync(uow, account, debits, ct);
        if (pending.Count == 0) return;

        // Only payments on THIS paying account, not reversed, and not already tied to another statement line.
        var linked = await uow.ReadSet<BankTransaction>()
            .Where(t => t.MatchedPaymentId != null)
            .Select(t => t.MatchedPaymentId!.Value)
            .ToListAsync(ct);
        var alreadyLinked = linked.ToHashSet();

        var settled = await (
            from p in uow.ReadSet<Payable>()
            where p.CompanyId == account.CompanyId
            from i in p.Installments
            from pay in i.Payments
            where pay.BankAccountId == account.Id && !pay.IsReversed
            select new SettledPaymentCandidate(pay.Id, pay.Amount, pay.PaymentDate))
            .ToListAsync(ct);

        var pool = settled.Where(c => !alreadyLinked.Contains(c.PaymentId)).ToList();
        if (pool.Count == 0) return;

        foreach (var t in pending)
        {
            if (ConciliationMatcher.FindSettledPayment(t.Amount, t.Date, pool) is { } paymentId)
            {
                t.LinkExistingPayment(paymentId);
                pool.RemoveAll(c => c.PaymentId == paymentId);
            }
        }
    }
}
