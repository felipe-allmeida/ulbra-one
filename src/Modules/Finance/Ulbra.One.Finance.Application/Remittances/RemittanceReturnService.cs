using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Processes CNAB240 return (.RET) files: parses entries, matches them to <see cref="RemittanceLine"/>s by
/// reference (via <see cref="ReturnEntryClassifier"/>), registers payments for paid lines via
/// <see cref="IPayablePaymentService"/>, releases rejected lines, and persists a
/// <see cref="RemittanceReturn"/> history record — all atomically.
/// </summary>
public sealed class RemittanceReturnService : IRemittanceReturnService
{
    private readonly IUnitOfWork _uow;
    private readonly ICnabReturnParser _parser;
    private readonly IReturnOutcomeResolver _outcomeResolver;
    private readonly IPayablePaymentService _paymentService;
    private readonly IFileStorageService _storage;
    private readonly TimeProvider _clock;
    private readonly ILogger<RemittanceReturnService> _logger;

    public RemittanceReturnService(
        IUnitOfWork uow,
        ICnabReturnParser parser,
        IReturnOutcomeResolver outcomeResolver,
        IPayablePaymentService paymentService,
        IFileStorageService storage,
        TimeProvider clock,
        ILogger<RemittanceReturnService> logger)
    {
        _uow = uow;
        _parser = parser;
        _outcomeResolver = outcomeResolver;
        _paymentService = paymentService;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    // Crítica accept codes (ORCAMENTO parity): anything else counts as a rejection. Unlike OutcomeFor
    // (movimento), the crítica is a binary accept/reject verdict per record — no "unknown" bucket.
    private static readonly IReadOnlySet<string> CriticismSuccessCodes =
        new HashSet<string>(StringComparer.Ordinal) { "00", "03", "BD", "BE", "BF" };

    /// <inheritdoc/>
    public async Task<Result<RemittanceReturnOutcome>> ProcessReturnAsync(
        Guid remittanceId,
        byte[] file,
        string fileName,
        string processedBy,
        Guid processedById,
        CancellationToken ct)
    {
        // Load and validate the remittance up front — it is the scope of all line matches.
        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == remittanceId, ct);

        if (remittance is null)
            return Result.Failure<RemittanceReturnOutcome>(
                Error.NotFound(ErrorCodes.Remittance.NotFound, $"Remittance {remittanceId} not found."));

        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var entries = _parser.Parse(file);

        // File-integrity gate: if any lote's declared trailer total diverges from the summed detail
        // values (truncated/corrupted/tampered return), refuse the whole import before any baixa —
        // nothing is persisted. The baixa here is automatic and atomic, so the check must precede the
        // loop below. Mirrors the legado, which blocks the operator's baixa on this same divergence.
        var trailerDivergences = _parser.CheckTrailers(file).Where(t => t.IsDivergent).ToList();
        if (trailerDivergences.Count > 0)
        {
            var detail = "Divergência de somatório no trailer de lote — arquivo recusado, nenhuma baixa efetuada. " +
                string.Join(" ", trailerDivergences.Select(t =>
                    $"Lote {t.Lote}: declarado {t.DeclaredTotal:F2}, somado {t.ActualTotal:F2}."));
            _logger.LogWarning("ProcessReturn: {Detail}", detail);
            return Result.Failure<RemittanceReturnOutcome>(
                Error.Validation(ErrorCodes.Remittance.ReturnTrailerDivergent, detail));
        }

        int matched = 0, paid = 0, returned = 0, unmatched = 0, divergent = 0;
        var notes = new List<string>();

        // Match + classify every entry against this remittance's lines (pure, no I/O), then apply.
        var classifications = ReturnEntryClassifier.Classify(entries, remittance.Lines, _outcomeResolver);

        foreach (var cls in classifications)
        {
            var entry = cls.Entry;

            switch (cls.Classification)
            {
                case ReturnClassification.AlreadyProcessed:
                    matched++;
                    _logger.LogDebug("ProcessReturn: reference '{Ref}' already processed — idempotent skip.", entry.Reference);
                    continue;
                case ReturnClassification.Unmatched:
                    unmatched++;
                    _logger.LogDebug("ProcessReturn: unmatched reference '{Ref}' in remittance {RemittanceId}.", entry.Reference, remittanceId);
                    continue;
                case ReturnClassification.Ambiguous:
                    divergent++;
                    notes.Add($"Ref {entry.Reference}: {cls.Note} — revisão manual.");
                    continue;
            }

            // Resolved to a Pending line (Paid / Rejected / Divergent).
            var line = cls.MatchedLine!;
            matched++;

            if (cls.Classification == ReturnClassification.Paid)
            {
                // Successful payment — load payable + installment and register via service.
                var payable = await _uow.Set<Payable>()
                    .Include(p => p.Installments)
                    .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

                var installment = payable?.FindInstallment(line.InstallmentId);

                if (payable is null || installment is null)
                {
                    _logger.LogWarning(
                        "ProcessReturn: line {Id} — payable {PayableId} or installment {InstallmentId} not found. Divergent.",
                        line.Id, line.PayableId, line.InstallmentId);
                    divergent++;
                    notes.Add($"Ref {entry.Reference}: payable or installment not found — left Pending.");
                    continue;
                }

                var effectiveDate = entry.ActualDate ?? today;
                var input = new RegisterPaymentInput(
                    PaymentDate: effectiveDate,
                    Amount: line.Amount,
                    BankAccountId: remittance.PayingBankAccountId,
                    Method: line.Method,
                    Interest: 0,
                    Fine: 0,
                    Discount: 0,
                    Notes: "CNAB retorno");

                var registerResult = await _paymentService.RegisterAsync(payable, installment, input, processedById, ct);

                if (registerResult.IsSuccess)
                {
                    line.MarkPaid(effectiveDate,
                        string.IsNullOrEmpty(entry.BankReference) ? null : entry.BankReference,
                        entry.AuthCode);
                    paid++;
                }
                else
                {
                    _logger.LogWarning(
                        "ProcessReturn: RegisterAsync failed for line {Id} ref '{Ref}': {Error}. Divergent.",
                        line.Id, entry.Reference, registerResult.Error);
                    divergent++;
                    notes.Add($"Ref {entry.Reference}: payment registration failed ({registerResult.Error.Code}: {registerResult.Error.Description}) — left Pending.");
                }
            }
            else if (cls.Classification == ReturnClassification.Rejected)
            {
                // Bank rejected — release installment back to Open.
                var payable = await _uow.Set<Payable>()
                    .Include(p => p.Installments)
                    .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

                var installment = payable?.FindInstallment(line.InstallmentId);

                if (installment is null)
                {
                    _logger.LogWarning(
                        "ProcessReturn: line {Id} rejected — installment {InstallmentId} not found; skipping release.",
                        line.Id, line.InstallmentId);
                }
                else if (installment.Status != InstallmentStatus.InRemittance)
                {
                    _logger.LogWarning(
                        "ProcessReturn: line {Id} rejected — installment {InstallmentId} in unexpected state {Status}; manual review required.",
                        line.Id, line.InstallmentId, installment.Status);
                    divergent++;
                    notes.Add($"Linha {line.Reference}: parcela em estado inesperado ({installment.Status}), revisão manual");
                    continue;
                }
                else
                {
                    installment.ReleaseFromRemittance();
                }

                // cls.Note carries the spelled-out occurrence reason (AEL-797) — e.g.
                // "AG (Agência/Conta Corrente/DV Inválido)" — so the reason is actionable.
                line.MarkReturned(cls.Note ?? "Bank rejection (no code)");
                returned++;
            }
            else
            {
                // Paid but value diverges, or Unknown outcome → leave Pending, flag as divergent.
                divergent++;
                var noteMsg = $"Ref {entry.Reference}: {cls.Note} — left Pending.";
                notes.Add(noteMsg);
                _logger.LogInformation("ProcessReturn: {Note}", noteMsg);
            }
        }

        // Recompute status on the remittance (all line mutations are scoped to it).
        remittance.Recompute();

        // Store the raw .RET file.
        var storageKey = await _storage.SaveAsync(fileName, new MemoryStream(file), ct);

        var returnRecord = RemittanceReturn.Create(
            companyId: remittance.CompanyId,
            payingBankAccountId: remittance.PayingBankAccountId,
            fileStorageKey: storageKey,
            fileName: fileName,
            matchedCount: matched,
            paidCount: paid,
            returnedCount: returned,
            unmatchedCount: unmatched,
            divergentCount: divergent,
            processedBy: processedBy,
            processedAt: now);

        await _uow.AddAsync(returnRecord, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(new RemittanceReturnOutcome(matched, paid, returned, unmatched, divergent, notes));
    }

    /// <inheritdoc/>
    public async Task<Result<RemittanceReturnPreviewResponse>> PreviewCompanyReturnAsync(
        Guid payingBankAccountId, byte[] file, CancellationToken ct)
    {
        var entries = _parser.Parse(file);

        // Trailer integrity — reported, NOT blocking (read-only preview; nothing is applied here).
        var trailerDivergences = _parser.CheckTrailers(file).Where(t => t.IsDivergent).ToList();
        var trailerMessages = trailerDivergences
            .Select(t => $"Lote {t.Lote}: declarado {t.DeclaredTotal:F2}, somado {t.ActualTotal:F2}.")
            .ToList();

        // Candidates: every line of this paying account's non-cancelled remittances. Scoping by the
        // account keeps "Seu Número" unique (NSA is per paying account), avoiding cross-account collisions.
        var remittances = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .Where(r => r.PayingBankAccountId == payingBankAccountId && r.Status != RemittanceStatus.Cancelled)
            .ToListAsync(ct);
        var candidates = remittances.SelectMany(r => r.Lines).ToList();

        var classifications = ReturnEntryClassifier.Classify(entries, candidates, _outcomeResolver);

        var header = _parser.ParseHeader(file);
        var payer = header is null
            ? null
            : new RemittanceReturnPayer(header.PayerDocument, header.Agency, header.Account);

        var previewEntries = classifications.Select(c => new RemittanceReturnPreviewEntry(
            Reference: c.Entry.Reference,
            FavoredName: c.MatchedLine?.FavoredName,
            FavoredDocument: c.MatchedLine?.FavoredDocument
                ?? (string.IsNullOrWhiteSpace(c.Entry.FavoredDocument) ? null : c.Entry.FavoredDocument),
            Method: c.MatchedLine?.Method.ToString(),
            ValorNominal: c.MatchedLine?.Amount,
            ValorEfetivado: c.Entry.ActualValue,
            EffectiveDate: c.Entry.ActualDate,
            OccurrenceCodes: c.Entry.OccurrenceCodes,
            Motivo: CnabReturnCodes.Explain(c.Entry.OccurrenceCodes),
            AuthCode: c.Entry.AuthCode,
            BankReference: string.IsNullOrWhiteSpace(c.Entry.BankReference) ? null : c.Entry.BankReference,
            Classification: c.Classification.ToString(),
            MatchMethod: c.MatchMethod.ToString(),
            RemittanceId: c.MatchedLine?.RemittanceId,
            LineId: c.MatchedLine?.Id,
            PayableId: c.MatchedLine?.PayableId,
            InstallmentId: c.MatchedLine?.InstallmentId,
            Note: c.Note)).ToList();

        var counters = new RemittanceReturnPreviewCounters(
            Total: classifications.Count,
            Paid: classifications.Count(c => c.Classification == ReturnClassification.Paid),
            Rejected: classifications.Count(c => c.Classification == ReturnClassification.Rejected),
            Divergent: classifications.Count(c => c.Classification == ReturnClassification.Divergent),
            Unmatched: classifications.Count(c => c.Classification == ReturnClassification.Unmatched),
            Ambiguous: classifications.Count(c => c.Classification == ReturnClassification.Ambiguous),
            AlreadyProcessed: classifications.Count(c => c.Classification == ReturnClassification.AlreadyProcessed));

        return Result.Success(new RemittanceReturnPreviewResponse(
            trailerDivergences.Count == 0, trailerMessages, payer, counters, previewEntries));
    }

    /// <inheritdoc/>
    public async Task<Result<RemittanceReturnPreviewResponse>> AutoConfirmPaidAndPreviewAsync(
        Guid companyId, Guid payingBankAccountId, byte[] file, string fileName,
        string processedBy, Guid processedById, CancellationToken ct)
    {
        var autoConfirmed = 0;

        // Only auto-apply when the file's trailer reconciles — a divergent trailer means the file is
        // suspect, so we fall through to a plain preview (nothing settled) and let the operator review.
        var trailerOk = !_parser.CheckTrailers(file).Any(t => t.IsDivergent);
        if (trailerOk)
        {
            var entries = _parser.Parse(file);
            var remittances = await _uow.Set<Remittance>()
                .Include(r => r.Lines)
                .Where(r => r.PayingBankAccountId == payingBankAccountId && r.Status != RemittanceStatus.Cancelled)
                .ToListAsync(ct);
            var candidates = remittances.SelectMany(r => r.Lines).ToList();
            var classifications = ReturnEntryClassifier.Classify(entries, candidates, _outcomeResolver);

            foreach (var c in classifications.Where(c =>
                c.Classification == ReturnClassification.Paid && c.MatchedLine is not null))
            {
                var bankRef = string.IsNullOrWhiteSpace(c.Entry.BankReference) ? null : c.Entry.BankReference;
                var applied = await ConfirmLinePaidAsync(
                    c.MatchedLine!.Id, processedById, ct, c.Entry.ActualDate, bankRef, c.Entry.AuthCode);
                if (applied.IsSuccess)
                    autoConfirmed++;
                else
                    _logger.LogWarning("Auto-confirm skipped line {LineId}: {Error}", c.MatchedLine!.Id, applied.Error.Code);
            }
        }

        // Re-project against the now-updated data: auto-confirmed lines surface as AlreadyProcessed, and the
        // remaining buckets (divergent/rejected/unmatched) keep their manual actions.
        var preview = await PreviewCompanyReturnAsync(payingBankAccountId, file, ct);
        if (preview.IsFailure)
            return preview;

        // Store the raw .RET and record what it did. This runs even when the trailer diverged and nothing was
        // applied — that file is the one the operator has to take back to the bank, so losing it is the worst case.
        var counters = preview.Value.Counters;
        var storageKey = await _storage.SaveAsync(fileName, new MemoryStream(file), ct);

        var returnRecord = RemittanceReturn.Create(
            companyId: companyId,
            payingBankAccountId: payingBankAccountId,
            fileStorageKey: storageKey,
            fileName: fileName,
            matchedCount: counters.Total - counters.Unmatched,
            paidCount: autoConfirmed,
            returnedCount: counters.Rejected,
            unmatchedCount: counters.Unmatched,
            // Ambiguous entries need the same manual review as a value divergence — ProcessReturnAsync folds
            // them together too, so the two paths report the same number for the same file.
            divergentCount: counters.Divergent + counters.Ambiguous,
            processedBy: processedBy,
            processedAt: _clock.GetUtcNow(),
            applied: trailerOk);

        await _uow.AddAsync(returnRecord, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(preview.Value with { AutoConfirmed = autoConfirmed });
    }

    /// <inheritdoc/>
    public async Task<Result> ConfirmLinePaidAsync(
        Guid remittanceLineId, Guid processedById, CancellationToken ct,
        DateOnly? effectiveDate = null, string? bankReference = null, string? authCode = null)
    {
        var line = await _uow.Set<RemittanceLine>()
            .FirstOrDefaultAsync(l => l.Id == remittanceLineId, ct);

        if (line is null)
            return Result.Failure(Error.NotFound(ErrorCodes.RemittanceLine.NotFound, "Remittance line not found."));

        if (line.Status != RemittanceLineStatus.Pending)
            return Result.Failure(Error.Failure(ErrorCodes.RemittanceLine.NotPending,
                $"Line is {line.Status} — only Pending lines can be manually confirmed."));

        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == line.RemittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Remittance.NotFound, "Remittance not found."));

        var payable = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

        var installment = payable?.FindInstallment(line.InstallmentId);

        if (payable is null || installment is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Payable.NotFound,
                "Payable or installment not found for this remittance line."));

        var effDate = effectiveDate ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var input = new RegisterPaymentInput(
            PaymentDate: effDate,
            Amount: line.Amount,
            BankAccountId: remittance.PayingBankAccountId,
            Method: line.Method,
            Interest: 0,
            Fine: 0,
            Discount: 0,
            Notes: bankReference is null ? "Confirmação manual" : "CNAB retorno");

        var registerResult = await _paymentService.RegisterAsync(payable, installment, input, processedById, ct);

        if (registerResult.IsFailure)
            return Result.Failure(registerResult.Error);

        line.MarkPaid(effDate, bankReference, authCode);
        remittance.Recompute();
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> MarkLineReturnedAsync(Guid remittanceLineId, string reason, CancellationToken ct)
    {
        var line = await _uow.Set<RemittanceLine>()
            .FirstOrDefaultAsync(l => l.Id == remittanceLineId, ct);

        if (line is null)
            return Result.Failure(Error.NotFound(ErrorCodes.RemittanceLine.NotFound, "Remittance line not found."));

        if (line.Status != RemittanceLineStatus.Pending)
            return Result.Failure(Error.Failure(ErrorCodes.RemittanceLine.NotPending,
                $"Line is {line.Status} — only Pending lines can be marked as returned."));

        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == line.RemittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Remittance.NotFound, "Remittance not found."));

        var payable = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

        var installment = payable?.FindInstallment(line.InstallmentId);

        if (installment is null)
        {
            _logger.LogWarning(
                "MarkLineReturned: installment {InstallmentId} not found for line {LineId}; skipping release.",
                line.InstallmentId, line.Id);
        }
        else if (installment.Status != InstallmentStatus.InRemittance)
        {
            return Result.Failure(Error.Failure(ErrorCodes.RemittanceLine.InstallmentNotInRemittance,
                $"Installment {line.InstallmentId} is in state {installment.Status} — expected InRemittance. Manual review required."));
        }
        else
        {
            installment.ReleaseFromRemittance();
        }

        line.MarkReturned(reason);
        remittance.Recompute();
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<RemittanceCriticismResponse>> PreviewCriticismAsync(
        Guid payingBankAccountId, byte[] file, CancellationToken ct)
    {
        var entries = _parser.Parse(file);

        // Candidates: lines of the account's non-cancelled remittances ("Seu Número" is unique per account),
        // indexed by Reference to match each crítica record and enable "remove from batch".
        var remittances = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .Where(r => r.PayingBankAccountId == payingBankAccountId && r.Status != RemittanceStatus.Cancelled)
            .ToListAsync(ct);
        var lineByReference = remittances.SelectMany(r => r.Lines)
            .GroupBy(l => l.Reference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var header = _parser.ParseHeader(file);
        var payer = header is null
            ? null
            : new RemittanceReturnPayer(header.PayerDocument, header.Agency, header.Account);

        var records = new List<RemittanceCriticismRecord>(entries.Count);
        foreach (var e in entries)
        {
            lineByReference.TryGetValue(e.Reference, out var line);
            // Accepted when every code is a success code (empty = blank = accepted).
            var isSuccess = e.OccurrenceCodes.All(CriticismSuccessCodes.Contains);
            // Crítica amount = Valor do Lançamento (settled value is zero); fall back to the system/real value.
            var amount = e.LaunchValue > 0 ? e.LaunchValue : (line?.Amount ?? e.ActualValue);

            records.Add(new RemittanceCriticismRecord(
                Reference: e.Reference,
                FavoredName: !string.IsNullOrWhiteSpace(line?.FavoredName)
                    ? line!.FavoredName
                    : (string.IsNullOrWhiteSpace(e.FavoredName) ? null : e.FavoredName),
                Amount: amount,
                OccurrenceCodes: e.OccurrenceCodes,
                Motivo: isSuccess ? null : CnabReturnCodes.Explain(e.OccurrenceCodes),
                IsSuccess: isSuccess,
                BankReference: string.IsNullOrWhiteSpace(e.BankReference) ? null : e.BankReference,
                RemittanceId: line?.RemittanceId,
                LineId: line?.Id,
                LineStatus: line?.Status.ToString()));
        }

        var success = records.Count(r => r.IsSuccess);
        return Result.Success(new RemittanceCriticismResponse(
            payer, records.Count, success, records.Count - success, records));
    }
}
