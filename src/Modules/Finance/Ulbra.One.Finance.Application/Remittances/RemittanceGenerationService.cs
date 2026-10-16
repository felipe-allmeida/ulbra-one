using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
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
    private readonly RemittanceOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<RemittanceGenerationService> _logger;

    public RemittanceGenerationService(
        IUnitOfWork uow,
        IRemittanceFileGenerator generator,
        IFileStorageService storage,
        TimeProvider clock,
        RemittanceOptions options,
        IHostEnvironment env,
        ILogger<RemittanceGenerationService> logger)
    {
        _uow = uow;
        _generator = generator;
        _storage = storage;
        _clock = clock;
        _options = options;
        _env = env;
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
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy,
        IReadOnlyList<Guid>? installmentIds, DateOnly? paymentDate, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await GenerateOnceAsync(companyId, payingBankAccountId, dueCutoff, generatedBy, installmentIds, paymentDate, ct);
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
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy,
        IReadOnlyList<Guid>? installmentIds, DateOnly? requestedPaymentDate, CancellationToken ct)
    {
        // Load paying bank account.
        var payingAccount = await _uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == payingBankAccountId && b.CompanyId == companyId, ct);

        if (payingAccount is null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.NotFound(ErrorCodes.Remittance.BankAccountNotFound, "Paying bank account not found."));

        if (!BankCodes.IsSantander(payingAccount.BankCode))
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure(ErrorCodes.Remittance.BankNotSupported, "Only Santander (033) is supported for CNAB remittance."));

        if (string.IsNullOrEmpty(payingAccount.CnabAgreementCode))
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure(ErrorCodes.Remittance.MissingAgreementCode,
                    "CNAB agreement code is not configured for this bank account."));

        // Load the company (CNPJ + legal name).
        var company = await _uow.ReadSet<Company>()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);

        if (company is null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.NotFound(ErrorCodes.Remittance.CompanyNotFound, "Company not found."));

        // The account may override the company's CNPJ/legal name (e.g. titled under a different
        // establishment CNPJ); each field independently falls back to the company when blank.
        var payerCnpj = string.IsNullOrEmpty(payingAccount.PayerCnpj) ? company.Cnpj : payingAccount.PayerCnpj;
        var payerLegalName = string.IsNullOrEmpty(payingAccount.PayerLegalName) ? company.LegalName : payingAccount.PayerLegalName;

        // Payer data must fit the fixed-width CNAB header fields. A bad payer blocks the whole
        // remittance (returned as a Result error instead of an unhandled overflow → HTTP 500).
        var payerReason = CnabFieldValidation.PayerReason(
            payerCnpj, payingAccount.Agency, payingAccount.AccountNumber, payingAccount.AccountDigit,
            payingAccount.AgencyDigit);
        if (payerReason is not null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.Failure(ErrorCodes.Remittance.InvalidPayerData, payerReason));

        // Load eligible payables: Scheduled + Open installments with DueDate <= cutoff.
        var payables = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .Where(p => p.CompanyId == companyId && p.Status == PayableStatus.Scheduled)
            .ToListAsync(ct);

        var now = _clock.GetUtcNow();
        // Date the bank executes the batch: the operator's choice when they scheduled it ahead, today otherwise.
        var paymentDate = requestedPaymentDate ?? DateOnly.FromDateTime(now.UtcDateTime);

        var skipped = new List<SkippedInstallment>();
        var pendingDrafts = new List<(Installment Inst, RemittanceLineDraft Draft)>();

        // Selection mode: the operator's checkbox selection replaces the cutoff window — a selected
        // future-dated installment rides; status/hold become explicit skips so the operator learns
        // why a checked row didn't make it.
        var selection = installmentIds is { Count: > 0 } ? installmentIds.ToHashSet() : null;

        foreach (var payable in payables)
        {
            var dueInstallments = payable.Installments
                .Where(i => selection != null
                    ? selection.Contains(i.Id)
                    : i.Status == InstallmentStatus.Open && !i.IsHeld && i.ScheduledPaymentDate <= dueCutoff)
                .ToList();

            if (dueInstallments.Count == 0)
                continue;

            // Load vendor once per payable (favored data is payable-scoped).
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
            var addr = vendor.Address;

            foreach (var inst in dueInstallments)
            {
                // In selection mode the status/hold filters were not applied above — surface them as skips.
                if (selection != null && inst.Status != InstallmentStatus.Open)
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "installment is not open"));
                    continue;
                }

                if (selection != null && inst.IsHeld)
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "installment is held"));
                    continue;
                }

                // Amount guard: a zero/negative installment would emit an all-zero value the bank rejects.
                if (inst.NetAmount <= 0)
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "installment amount must be greater than zero"));
                    continue;
                }

                // Resolve the payment method PER INSTALLMENT (AEL-933): the forma the requisition authored on the
                // parcela wins; a manual payable falls back to its payable-level method. No account default.
                var method = inst.PaymentMethod ?? payable.PaymentMethod;

                if (method is null)
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "no payment method"));
                    continue;
                }

                if (method is not (PaymentMethod.Pix or PaymentMethod.Ted or PaymentMethod.Boleto))
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "method not supported by CNAB"));
                    continue;
                }

                // Effective PIX key (AEL-938): the per-payment override wins over the vendor's registered key,
                // which is only a fallback and is never altered. Empty override → the vendor's key is used.
                var effectivePixKey = string.IsNullOrWhiteSpace(inst.PixKeyOverride) ? bankInfo.PixKey : inst.PixKeyOverride.Trim();

                // PIX/TED require vendor bank/PIX data present; boleto rides the installment's linha digitável.
                if (method is PaymentMethod.Pix or PaymentMethod.Ted)
                {
                    var bankInfoReason = RemittanceEligibility.Evaluate(payingAccount, method, bankInfo, pixKeyOverride: inst.PixKeyOverride);
                    if (bankInfoReason == RemittanceManualReason.VendorBankInfoMissing)
                    {
                        skipped.Add(new SkippedInstallment(payable.Id, inst.Id,
                            method == PaymentMethod.Ted ? "missing vendor bank account" : "missing vendor PIX key"));
                        continue;
                    }
                }

                // Width/format guard: malformed vendor data becomes a clean skip instead of an
                // unhandled overflow in CnabRecordBuilder (which would surface as HTTP 500).
                var invalidReason = CnabFieldValidation.FavoredReason(method.Value, vendor.Document, bankInfo, pixKeyOverride: inst.PixKeyOverride);
                if (invalidReason is not null)
                {
                    skipped.Add(new SkippedInstallment(payable.Id, inst.Id, invalidReason));
                    continue;
                }

                // Boleto: the linha digitável lives on the installment — validate it per-installment.
                if (method == PaymentMethod.Boleto)
                {
                    var boletoReason = CnabFieldValidation.BoletoReason(inst.BoletoDigitableLine);
                    if (boletoReason is not null)
                    {
                        skipped.Add(new SkippedInstallment(payable.Id, inst.Id,
                            string.IsNullOrWhiteSpace(inst.BoletoDigitableLine) ? "missing boleto linha digitável" : boletoReason));
                        continue;
                    }
                }

                pendingDrafts.Add((inst, new RemittanceLineDraft(
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
                    FavoredPixKey: effectivePixKey,
                    DueDate: inst.DueDate,
                    BoletoDigitableLine: inst.BoletoDigitableLine,
                    FavoredAddressStreet: addr.Street,
                    FavoredAddressNumber: addr.Number,
                    FavoredAddressComplement: addr.Complement ?? string.Empty,
                    FavoredAddressNeighborhood: addr.Neighborhood,
                    FavoredAddressCity: addr.City,
                    FavoredAddressCep: addr.Cep,
                    FavoredAddressState: addr.State)));
            }
        }

        // Selected ids that never appeared (wrong company, not Scheduled, or plain unknown) — report
        // instead of silently dropping the operator's checkbox.
        if (selection != null)
        {
            var seen = pendingDrafts.Select(d => d.Inst.Id)
                .Concat(skipped.Select(s => s.InstallmentId))
                .ToHashSet();
            foreach (var id in selection.Where(id => !seen.Contains(id)))
                skipped.Add(new SkippedInstallment(Guid.Empty, id, "installment not found among Scheduled payables of this company"));
        }

        // Guard: nothing eligible — return failure without any side effects. The description names why
        // each installment was dropped (pt-BR, shown verbatim by the UI) so the operator can act.
        if (pendingDrafts.Count == 0)
            return Result.Failure<GenerateRemittanceOutcome>(
                // Keep the new stable error code, but the detail stays the diagnosable pt-BR breakdown (Onda 5):
                // CreateRemittance passes Description through as the RFC7807 detail, shown verbatim by the UI.
                Error.Failure(ErrorCodes.Remittance.NothingToRemit, NothingToRemitDescription.Build(skipped, dueCutoff)));

        // Partition into one file per payment category (AEL-933): PIX / TED / boleto bancário / convênio.
        // Each category becomes its own Remittance + file, so the operator sees exactly what forma each
        // remittance carries; the writer emits a single lote per file.
        var groups = pendingDrafts
            .GroupBy(d => RemittanceCategoryResolver.Resolve(d.Draft.Method, d.Draft.BoletoDigitableLine))
            .OrderBy(g => g.Key)
            .ToList();

        // NSA base for this paying account; each category file takes the next sequential number. A collision
        // with a concurrent generation throws DbUpdateException on save and the whole batch retries (fresh read).
        var maxSeq = await _uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == payingBankAccountId)
            .Select(r => (int?)r.SequenceNumber)
            .MaxAsync(ct) ?? 0;

        var payer = new RemittancePayerInfo(
            Cnpj: payerCnpj,
            LegalName: payerLegalName,
            Agency: payingAccount.Agency,
            AccountNumber: payingAccount.AccountNumber,
            AccountDigit: payingAccount.AccountDigit,
            AgreementCode: payingAccount.CnabAgreementCode,
            AgencyDigit: payingAccount.AgencyDigit,
            AddressStreet: payingAccount.AddressStreet,
            AddressNumber: payingAccount.AddressNumber,
            AddressComplement: payingAccount.AddressComplement,
            AddressCity: payingAccount.AddressCity,
            AddressCep: payingAccount.AddressCep,
            AddressState: payingAccount.AddressState);

        // Per-environment NSA (Santander note G010: NSA 1-10 = test, validated but never paid). The
        // persisted SequenceNumber stays monotonic/unique; only the file NSA is derived from it.
        var isProduction = _options.ProductionRemittance ?? _env.IsProduction();

        var remittances = new List<Remittance>(groups.Count);
        var sequenceNumber = maxSeq;

        foreach (var group in groups)
        {
            sequenceNumber++;
            var fileNsa = RemittanceNsa.ForFile(sequenceNumber, isProduction);
            var remittance = Remittance.Create(companyId, payingBankAccountId, payingAccount.BankCode,
                sequenceNumber, generatedBy, now, group.Key, paymentDate);

            var fileLines = new List<RemittanceFileLine>();
            foreach (var (inst, draft) in group)
            {
                inst.MarkInRemittance();
                var line = remittance.AddLine(draft);
                fileLines.Add(ToFileLine(draft, paymentDate, line.Reference));
            }

            var fileInput = new RemittanceFileInput(
                BankCode: payingAccount.BankCode,
                SequenceNumber: fileNsa,
                GeneratedAt: now,
                Payer: payer,
                Lines: fileLines);

            var bytes = _generator.Generate(fileInput);
            // File name carries the forma + NSA (same value written to the header) so files from one run
            // never collide on the timestamp.
            var fileName = $"SANTANDER_REMESSA_{FileTag(group.Key)}_{fileNsa:D6}_{now:yyyyMMddHHmmss}.txt";
            var key = await _storage.SaveAsync(fileName, new MemoryStream(bytes), ct);
            remittance.SetFile(key, fileName, bytes.Length);

            await _uow.AddAsync(remittance, ct);
            remittances.Add(remittance);
        }

        await _uow.SaveChangesAsync(ct);

        return Result.Success(new GenerateRemittanceOutcome(remittances, skipped));
    }

    /// <summary>Upper-case forma tag embedded in the remittance file name.</summary>
    private static string FileTag(RemittanceCategory category) => category switch
    {
        RemittanceCategory.Pix => "PIX",
        RemittanceCategory.Ted => "TED",
        RemittanceCategory.BoletoBancario => "BOLETO",
        RemittanceCategory.Convenio => "CONVENIO",
        _ => "REMESSA",
    };

    /// <summary>Maps a persisted line draft to the pure file-generation line, stamping the payment date and Reference.</summary>
    private static RemittanceFileLine ToFileLine(RemittanceLineDraft d, DateOnly paymentDate, string reference) => new(
        Method: d.Method,
        Amount: d.Amount,
        PaymentDate: paymentDate,
        FavoredName: d.FavoredName,
        FavoredDocument: d.FavoredDocument,
        FavoredBankCode: d.FavoredBankCode,
        FavoredAgency: d.FavoredAgency,
        FavoredAccount: d.FavoredAccount,
        FavoredAccountDigit: d.FavoredAccountDigit,
        FavoredPixKey: d.FavoredPixKey,
        Reference: reference,
        BoletoDigitableLine: d.BoletoDigitableLine,
        DueDate: d.DueDate,
        FavoredAddressStreet: d.FavoredAddressStreet,
        FavoredAddressNumber: d.FavoredAddressNumber,
        FavoredAddressComplement: d.FavoredAddressComplement,
        FavoredAddressNeighborhood: d.FavoredAddressNeighborhood,
        FavoredAddressCity: d.FavoredAddressCity,
        FavoredAddressCep: d.FavoredAddressCep,
        FavoredAddressState: d.FavoredAddressState);

    public async Task<Result> CancelAsync(Guid remittanceId, CancellationToken ct)
    {
        var remittance = await _uow.Set<Remittance>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == remittanceId, ct);

        if (remittance is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Remittance.NotFound, "Remittance not found."));

        if (remittance.Status != RemittanceStatus.Generated)
            return Result.Failure(Error.Failure(ErrorCodes.Remittance.NotGenerated,
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
