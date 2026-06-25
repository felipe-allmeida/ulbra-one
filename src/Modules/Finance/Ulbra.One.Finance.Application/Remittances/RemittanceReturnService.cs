using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Processes CNAB240 return (.RET) files: parses entries, matches them to <see cref="RemittanceLine"/>s by
/// reference, registers payments for paid lines via <see cref="IPayablePaymentService"/>, releases
/// rejected lines, and persists a <see cref="RemittanceReturn"/> history record — all atomically.
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

    /// <inheritdoc/>
    public async Task<Result<RemittanceReturnOutcome>> ProcessReturnAsync(
        byte[] file,
        string fileName,
        string processedBy,
        Guid processedById,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var entries = _parser.Parse(file);

        int matched = 0, paid = 0, returned = 0, unmatched = 0, divergent = 0;
        var notes = new List<string>();

        // Track which remittances were touched so we can Recompute() each once.
        var touchedRemittances = new Dictionary<Guid, Remittance>();

        foreach (var entry in entries)
        {
            // Match by Reference (tracked — we will mutate the line).
            // Load all candidates, then prefer the one whose installment is InRemittance (actively in a
            // live remittance that hasn't been reconciled). This resolves ambiguity when multiple
            // remittances share the same sequence-scoped reference (e.g. in integration-test databases
            // with many companies, or when a reference was reused after a cancel+resubmit cycle).
            // If no InRemittance candidate is found, fall back to the first Pending line to handle
            // re-processing of a return file (idempotency).
            var candidates = await _uow.Set<RemittanceLine>()
                .Where(l => l.Reference == entry.Reference && l.Status == RemittanceLineStatus.Pending)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync(ct);

            // Among Pending candidates, prefer lines whose installment is InRemittance.
            RemittanceLine? line = null;
            foreach (var candidate in candidates)
            {
                var installment = await _uow.ReadSet<Installment>()
                    .FirstOrDefaultAsync(i => i.Id == candidate.InstallmentId, ct);
                if (installment?.Status == InstallmentStatus.InRemittance)
                {
                    line = candidate;
                    break;
                }
            }
            // Fall back: any Pending candidate.
            line ??= candidates.FirstOrDefault();

            if (line is null)
            {
                // No Pending match — check for an already-processed line (idempotent re-run).
                var processedLine = await _uow.Set<RemittanceLine>()
                    .FirstOrDefaultAsync(l => l.Reference == entry.Reference
                        && l.Status != RemittanceLineStatus.Pending, ct);
                if (processedLine is not null)
                {
                    matched++;
                    _logger.LogDebug("ProcessReturn: line {Id} reference '{Ref}' already {Status} — idempotent skip.",
                        processedLine.Id, entry.Reference, processedLine.Status);
                    continue;
                }

                unmatched++;
                _logger.LogDebug("ProcessReturn: unmatched reference '{Ref}'.", entry.Reference);
                continue;
            }

            // At this point line.Status == Pending (guaranteed by the candidates query).
            matched++;

            // Load the remittance (tracked) to Recompute() later.
            if (!touchedRemittances.TryGetValue(line.RemittanceId, out var remittance))
            {
                remittance = await _uow.Set<Remittance>()
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == line.RemittanceId, ct);

                if (remittance is null)
                {
                    _logger.LogWarning(
                        "ProcessReturn: line {Id} references remittance {RemittanceId} which does not exist — skipping line.",
                        line.Id, line.RemittanceId);
                    unmatched++;
                    matched--;
                    continue;
                }

                touchedRemittances[remittance.Id] = remittance;
            }

            var outcome = _outcomeResolver.OutcomeFor(entry.OccurrenceCodes);

            if (outcome == ReturnOutcome.Paid && entry.ActualValue == line.Amount)
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
                    line.MarkPaid(effectiveDate, string.IsNullOrEmpty(entry.BankReference) ? null : entry.BankReference);
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
            else if (outcome == ReturnOutcome.Rejected)
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

                var reason = entry.OccurrenceCodes.Count > 0
                    ? string.Join(", ", entry.OccurrenceCodes)
                    : "Bank rejection (no code)";
                line.MarkReturned(reason);
                returned++;
            }
            else
            {
                // Paid but value diverges, or Unknown outcome → leave Pending, flag as divergent.
                divergent++;
                var noteMsg = outcome == ReturnOutcome.Paid
                    ? $"Ref {entry.Reference}: value divergence (expected {line.Amount:F2}, actual {entry.ActualValue:F2}) — left Pending."
                    : $"Ref {entry.Reference}: unknown occurrence codes [{string.Join(",", entry.OccurrenceCodes)}] — left Pending for manual review.";
                notes.Add(noteMsg);
                _logger.LogInformation("ProcessReturn: {Note}", noteMsg);
            }
        }

        // Recompute status on all touched remittances.
        foreach (var remittance in touchedRemittances.Values)
            remittance.Recompute();

        // Store the raw .RET file.
        var storageKey = await _storage.SaveAsync(fileName, new MemoryStream(file), ct);

        // Determine companyId from the first touched remittance (or Guid.Empty if none).
        var companyId = touchedRemittances.Values.FirstOrDefault()?.CompanyId ?? Guid.Empty;

        var returnRecord = RemittanceReturn.Create(
            companyId: companyId,
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
    public async Task<Result> ConfirmLinePaidAsync(Guid remittanceLineId, Guid processedById, CancellationToken ct)
    {
        var line = await _uow.Set<RemittanceLine>()
            .FirstOrDefaultAsync(l => l.Id == remittanceLineId, ct);

        if (line is null)
            return Result.Failure(Error.NotFound("RemittanceLine.NotFound", "Remittance line not found."));

        if (line.Status != RemittanceLineStatus.Pending)
            return Result.Failure(Error.Failure("RemittanceLine.NotPending",
                $"Line is {line.Status} — only Pending lines can be manually confirmed."));

        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == line.RemittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound("Remittance.NotFound", "Remittance not found."));

        var payable = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

        var installment = payable?.FindInstallment(line.InstallmentId);

        if (payable is null || installment is null)
            return Result.Failure(Error.NotFound("Payable.NotFound",
                "Payable or installment not found for this remittance line."));

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var input = new RegisterPaymentInput(
            PaymentDate: today,
            Amount: line.Amount,
            BankAccountId: remittance.PayingBankAccountId,
            Method: line.Method,
            Interest: 0,
            Fine: 0,
            Discount: 0,
            Notes: "Confirmação manual");

        var registerResult = await _paymentService.RegisterAsync(payable, installment, input, processedById, ct);

        if (registerResult.IsFailure)
            return Result.Failure(registerResult.Error);

        line.MarkPaid(today, null);
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
            return Result.Failure(Error.NotFound("RemittanceLine.NotFound", "Remittance line not found."));

        if (line.Status != RemittanceLineStatus.Pending)
            return Result.Failure(Error.Failure("RemittanceLine.NotPending",
                $"Line is {line.Status} — only Pending lines can be marked as returned."));

        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == line.RemittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound("Remittance.NotFound", "Remittance not found."));

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
            return Result.Failure(Error.Failure("RemittanceLine.InstallmentNotInRemittance",
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
}
