using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Selects eligible installments, marks them InRemittance, builds the Remittance aggregate,
/// generates the CNAB240 file and stores it — all in one atomic UoW commit.
/// </summary>
public sealed class RemittanceGenerationService : IRemittanceGenerationService
{
    private readonly IUnitOfWork _uow;
    private readonly IRemittanceFileGenerator _generator;
    private readonly IFileStorageService _storage;
    private readonly TimeProvider _clock;
    private readonly ILogger<RemittanceGenerationService> _logger;

    public RemittanceGenerationService(
        IUnitOfWork uow,
        IRemittanceFileGenerator generator,
        IFileStorageService storage,
        TimeProvider clock,
        ILogger<RemittanceGenerationService> logger)
    {
        _uow = uow;
        _generator = generator;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    private const int MaxGenerationAttempts = 5;

    /// <summary>
    /// Public entry point. Retries on a unique-index violation of the per-account remittance sequence
    /// number — which happens when the daily sweep and a manual generation (or two manual generations)
    /// race for the same (company, paying account). Each attempt re-reads fresh state, so installments
    /// the winner already moved to <c>InRemittance</c> are naturally skipped: the retry is safe and
    /// idempotent, and it also covers a concurrent installment change (DbUpdateConcurrencyException).
    /// </summary>
    public async Task<Result<GenerateRemittanceOutcome>> GenerateAsync(
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await GenerateOnceAsync(companyId, payingBankAccountId, dueCutoff, generatedBy, ct);
            }
            catch (DbUpdateException) when (attempt < MaxGenerationAttempts)
            {
                // Lost the sequence-number race against a concurrent generation. Discard the in-memory
                // changes and re-run against fresh state.
                _uow.ClearChangeTracker();
            }
        }
    }

    private async Task<Result<GenerateRemittanceOutcome>> GenerateOnceAsync(
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy, CancellationToken ct)
    {
        // Load paying bank account.
        var payingAccount = await _uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == payingBankAccountId && b.CompanyId == companyId, ct);

        if (payingAccount is null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.NotFound("Remittance.BankAccountNotFound", "Paying bank account not found."));

        if (!BankCodes.IsSantander(payingAccount.BankCode))
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure("Remittance.BankNotSupported", "Only Santander (033) is supported for CNAB remittance."));

        if (string.IsNullOrEmpty(payingAccount.CnabAgreementCode))
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure("Remittance.MissingAgreementCode",
                    "CNAB agreement code is not configured for this bank account."));

        // Load the company (CNPJ + legal name).
        var company = await _uow.ReadSet<Company>()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);

        if (company is null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.NotFound("Remittance.CompanyNotFound", "Company not found."));

        // Payer data must fit the fixed-width CNAB header fields. A bad payer blocks the whole
        // remittance (returned as a Result error instead of an unhandled overflow → HTTP 500).
        var payerReason = CnabFieldValidation.PayerReason(
            company.Cnpj, payingAccount.Agency, payingAccount.AccountNumber, payingAccount.AccountDigit);
        if (payerReason is not null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure("Remittance.InvalidPayerData", payerReason));

        // Load eligible payables: Scheduled + Open installments with DueDate <= cutoff.
        var payables = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .Where(p => p.CompanyId == companyId && p.Status == PayableStatus.Scheduled)
            .ToListAsync(ct);

        var now = _clock.GetUtcNow();
        var paymentDate = DateOnly.FromDateTime(now.UtcDateTime);

        var skipped = new List<SkippedInstallment>();
        var pendingDrafts = new List<(Installment Inst, RemittanceLineDraft Draft, RemittanceFileLine FileLine)>();

        foreach (var payable in payables)
        {
            var dueInstallments = payable.Installments
                .Where(i => i.Status == InstallmentStatus.Open && !i.IsHeld && i.ScheduledPaymentDate <= dueCutoff)
                .ToList();

            if (dueInstallments.Count == 0)
                continue;

            // Resolve payment method.
            var method = payable.PaymentMethod ?? payingAccount.DefaultPaymentMethod;

            if (method is null)
            {
                foreach (var inst in dueInstallments)
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "no payment method"));
                continue;
            }

            if (method is not (PaymentMethod.Pix or PaymentMethod.Ted))
            {
                foreach (var inst in dueInstallments)
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "method not supported by CNAB"));
                continue;
            }

            // Load vendor.
            var vendor = await _uow.ReadSet<VendorEntity>()
                .FirstOrDefaultAsync(v => v.Id == payable.VendorId, ct);

            if (vendor is null)
            {
                foreach (var inst in dueInstallments)
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "vendor not found"));
                continue;
            }

            var bankInfo = vendor.BankInfo;
            var favoredBankCode = BankCodes.Normalize(bankInfo.BankCode);

            // Validate bank info per method via shared eligibility helper.
            // Bank/agreement/method were already pre-screened above (and by the candidate-account query), so only VendorBankInfoMissing can surface here.
            var bankInfoReason = RemittanceEligibility.Evaluate(payingAccount, method, bankInfo);
            if (bankInfoReason == RemittanceManualReason.VendorBankInfoMissing)
            {
                var skipReason = method == PaymentMethod.Ted ? "missing vendor bank account" : "missing vendor PIX key";
                foreach (var inst in dueInstallments)
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, skipReason));
                continue;
            }

            // Width/format guard: malformed vendor data becomes a clean skip instead of an
            // unhandled overflow in CnabRecordBuilder (which would surface as HTTP 500).
            var invalidReason = CnabFieldValidation.FavoredReason(method.Value, vendor.Document, bankInfo);
            if (invalidReason is not null)
            {
                foreach (var inst in dueInstallments)
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, invalidReason));
                continue;
            }

            // All checks passed — stage each installment (not yet marked or saved).
            foreach (var inst in dueInstallments)
            {
                var draft = new RemittanceLineDraft(
                    PayableId: payable.Id,
                    InstallmentId: inst.Id,
                    Method: method.Value,
                    Amount: inst.NetAmount,
                    FavoredName: vendor.Name,
                    FavoredDocument: vendor.Document,
                    FavoredBankCode: favoredBankCode,
                    FavoredAgency: bankInfo.Branch,
                    FavoredAccount: bankInfo.Account,
                    FavoredAccountDigit: bankInfo.AccountDigit,
                    FavoredPixKey: bankInfo.PixKey);

                // Placeholder FileLine — Reference assigned after Remittance.Create below.
                var fileLine = new RemittanceFileLine(
                    Method: method.Value,
                    Amount: inst.NetAmount,
                    PaymentDate: paymentDate,
                    FavoredName: vendor.Name,
                    FavoredDocument: vendor.Document,
                    FavoredBankCode: favoredBankCode,
                    FavoredAgency: bankInfo.Branch,
                    FavoredAccount: bankInfo.Account,
                    FavoredAccountDigit: bankInfo.AccountDigit,
                    FavoredPixKey: bankInfo.PixKey,
                    Reference: string.Empty);

                pendingDrafts.Add((inst, draft, fileLine));
            }
        }

        // Guard: nothing eligible — return failure without any side effects.
        if (pendingDrafts.Count == 0)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure("Remittance.NothingToRemit",
                    "No eligible installments found for remittance. All were skipped or no payables are due."));

        // Compute sequence number: 1 + max existing for this paying account.
        var maxSeq = await _uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == payingBankAccountId)
            .Select(r => (int?)r.SequenceNumber)
            .MaxAsync(ct) ?? 0;
        var sequenceNumber = maxSeq + 1;

        var remittance = Remittance.Create(companyId, payingBankAccountId, payingAccount.BankCode,
            sequenceNumber, generatedBy, now);

        var fileLines = new List<RemittanceFileLine>(pendingDrafts.Count);

        foreach (var (inst, draft, _) in pendingDrafts)
        {
            inst.MarkInRemittance();
            var line = remittance.AddLine(draft);
            fileLines.Add(new RemittanceFileLine(
                Method: draft.Method,
                Amount: draft.Amount,
                PaymentDate: paymentDate,
                FavoredName: draft.FavoredName,
                FavoredDocument: draft.FavoredDocument,
                FavoredBankCode: draft.FavoredBankCode,
                FavoredAgency: draft.FavoredAgency,
                FavoredAccount: draft.FavoredAccount,
                FavoredAccountDigit: draft.FavoredAccountDigit,
                FavoredPixKey: draft.FavoredPixKey,
                Reference: line.Reference));
        }

        // Generate the CNAB240 file bytes.
        var payer = new RemittancePayerInfo(
            Cnpj: company.Cnpj,
            LegalName: company.LegalName,
            Agency: payingAccount.Agency,
            AccountNumber: payingAccount.AccountNumber,
            AccountDigit: payingAccount.AccountDigit,
            AgreementCode: payingAccount.CnabAgreementCode);

        var fileInput = new RemittanceFileInput(
            BankCode: payingAccount.BankCode,
            SequenceNumber: sequenceNumber,
            GeneratedAt: now,
            Payer: payer,
            Lines: fileLines);

        var bytes = _generator.Generate(fileInput);
        var fileName = $"remessa-{sequenceNumber}.rem";
        var key = await _storage.SaveAsync(fileName, new MemoryStream(bytes), ct);
        remittance.SetFile(key, fileName, bytes.Length);

        await _uow.AddAsync(remittance, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(new GenerateRemittanceOutcome(remittance, skipped));
    }

    public async Task<Result> CancelAsync(Guid remittanceId, CancellationToken ct)
    {
        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == remittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound("Remittance.NotFound", "Remittance not found."));

        if (remittance.Status != RemittanceStatus.Generated)
            return Result.Failure(Error.Failure("Remittance.NotGenerated",
                "Only a Generated remittance can be cancelled."));

        foreach (var line in remittance.Lines)
        {
            var payable = await _uow.Set<Payable>()
                .Include(p => p.Installments)
                .FirstOrDefaultAsync(p => p.Id == line.PayableId, ct);

            var installment = payable?.FindInstallment(line.InstallmentId);

            if (installment is null)
            {
                _logger.LogWarning(
                    "CancelAsync: remittance {RemittanceId} — could not find installment {InstallmentId} for payable {PayableId}; skipping release.",
                    remittanceId, line.InstallmentId, line.PayableId);
                continue;
            }

            installment.ReleaseFromRemittance();
        }

        remittance.Cancel();
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
