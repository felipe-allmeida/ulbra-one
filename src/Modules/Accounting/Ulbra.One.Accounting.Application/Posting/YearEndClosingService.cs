using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Endpoints.Reports;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Accounting.Application.Posting;

/// <summary>Posts the year-end closing entries (Origin = Closing, competence 31/12) and locks the exercise.
/// The result is computed exactly as the DRE computes it (same loader + calculator, which exclude Closing
/// entries), so the amount transferred to Lucros/Prejuízos Acumulados equals the DRE's Lucro Líquido. Idempotent
/// per (company, fiscal year) via the apuração SourceKey. Mutations are staged on the UoW; the endpoint saves.</summary>
public sealed class YearEndClosingService(IUnitOfWork uow, TimeProvider clock, AccountingPostingOptions opts)
    : IYearEndClosingService
{
    public async Task<Result<YearEndClosingResult>> CloseYearAsync(Guid companyId, int fiscalYear, CancellationToken ct)
    {
        if (companyId == Guid.Empty)
            return Result.Failure<YearEndClosingResult>(Error.Validation(ErrorCodes.YearEndClosing.CompanyRequired, "Empresa é obrigatória."));
        if (fiscalYear < 1)
            return Result.Failure<YearEndClosingResult>(Error.Validation(ErrorCodes.YearEndClosing.InvalidYear, "Exercício inválido."));

        var apuracaoKey = $"year-end-closing:{companyId}:{fiscalYear}:apuracao";
        var transferKey = $"year-end-closing:{companyId}:{fiscalYear}:transfer";

        // Idempotency: the exercise was already closed → no-op.
        var alreadyClosed = await uow.ReadSet<JournalEntry>()
            .AnyAsync(e => e.CompanyId == companyId && e.SourceKey == apuracaoKey && e.DeletedAt == null, ct);
        if (alreadyClosed)
            return Result.Success(new YearEndClosingResult(AlreadyClosed: true, NetIncome: 0m, ResultAccountsCleared: 0));

        // Compute the exercise's result the same way the DRE does (per account contribution = credit − debit,
        // excluding prior Closing entries) so the cleared/transferred amount matches the DRE's Lucro Líquido.
        var from = new DateOnly(fiscalYear, 1, 1);
        var to = new DateOnly(fiscalYear, 12, 31);
        var lines = await PostedLinesLoader.LoadAsync(uow, companyId, ct);
        var dre = IncomeStatementCalculator.Build(lines, from, to);
        var resultLines = dre.Sections.SelectMany(s => s.Lines).Where(l => l.Amount != 0m).ToList();
        var netIncome = dre.NetIncome;

        // Transitory + retained-earnings accounts must be active analytical accounts in the global chart.
        var apuracao = await ResolvePostableAsync(opts.ApuracaoCode, ct);
        if (apuracao is null)
            return Result.Failure<YearEndClosingResult>(Error.Validation(ErrorCodes.YearEndClosing.ApuracaoAccountMissing,
                $"Conta de Apuração do Resultado ('{opts.ApuracaoCode}') não encontrada ou não postável."));
        var retained = await ResolvePostableAsync(opts.RetainedEarningsCode, ct);
        if (retained is null)
            return Result.Failure<YearEndClosingResult>(Error.Validation(ErrorCodes.YearEndClosing.RetainedEarningsAccountMissing,
                $"Conta de Lucros/Prejuízos Acumulados ('{opts.RetainedEarningsCode}') não encontrada ou não postável."));

        var now = clock.GetUtcNow();
        var reference = $"Encerramento {fiscalYear}";
        var number = await NextNumberAsync(companyId, ct);

        // 1) Apuração: zero each result account into the transitory account.
        if (resultLines.Count > 0)
        {
            var apuracaoEntry = new JournalEntry(companyId, to, "Apuração do resultado do exercício",
                JournalOrigin.Closing, sourceReference: reference, sourceKey: apuracaoKey);
            foreach (var l in resultLines)
            {
                // l.Amount is the net credit position (credit − debit); post the opposite side to zero it.
                var side = l.Amount > 0m ? EntrySide.Debit : EntrySide.Credit;
                apuracaoEntry.AddLine(new JournalLine(l.LedgerAccountId, side, Math.Abs(l.Amount)));
            }
            if (netIncome != 0m)
                apuracaoEntry.AddLine(new JournalLine(apuracao.Id,
                    netIncome > 0m ? EntrySide.Credit : EntrySide.Debit, Math.Abs(netIncome)));
            apuracaoEntry.Post(number++, now);
            await uow.AddAsync(apuracaoEntry, ct);
        }

        // 2) Transfer: move the net result from Apuração to Lucros/Prejuízos Acumulados.
        if (netIncome != 0m)
        {
            var transfer = new JournalEntry(companyId, to, "Transferência do resultado ao patrimônio líquido",
                JournalOrigin.Closing, sourceReference: reference, sourceKey: transferKey);
            var amount = Math.Abs(netIncome);
            // Profit: debit Apuração, credit retained earnings. Loss: credit Apuração, debit retained earnings.
            transfer.AddLine(new JournalLine(apuracao.Id, netIncome > 0m ? EntrySide.Debit : EntrySide.Credit, amount));
            transfer.AddLine(new JournalLine(retained.Id, netIncome > 0m ? EntrySide.Credit : EntrySide.Debit, amount));
            transfer.Post(number++, now);
            await uow.AddAsync(transfer, ct);
        }

        // 3) Lock the exercise: close all 12 months. Postings into a closed month are blocked (manual) or
        //    clamped to the current open period (automatic CaP), so the closed exercise stays frozen.
        for (var month = 1; month <= 12; month++)
        {
            var period = await uow.Set<AccountingPeriod>().FirstOrDefaultAsync(
                p => p.CompanyId == companyId && p.FiscalYear == fiscalYear && p.Month == month, ct);
            if (period is null)
            {
                period = new AccountingPeriod(companyId, fiscalYear, month);
                await uow.AddAsync(period, ct);
            }
            period.Close();
        }

        return Result.Success(new YearEndClosingResult(false, netIncome, resultLines.Count));
    }

    private async Task<LedgerAccount?> ResolvePostableAsync(string code, CancellationToken ct)
    {
        var a = await uow.ReadSet<LedgerAccount>().FirstOrDefaultAsync(x => x.Code == code && x.DeletedAt == null, ct);
        return a is { } acct && acct.CanReceiveEntry ? acct : null;
    }

    private async Task<int> NextNumberAsync(Guid companyId, CancellationToken ct) =>
        (await uow.ReadSet<JournalEntry>()
            .Where(e => e.CompanyId == companyId && e.Number != null)
            .MaxAsync(e => (int?)e.Number, ct) ?? 0) + 1;
}
