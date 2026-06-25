using Microsoft.EntityFrameworkCore;
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

    public RemittanceGenerationService(
        IUnitOfWork uow,
        IRemittanceFileGenerator generator,
        IFileStorageService storage,
        TimeProvider clock)
    {
        _uow = uow;
        _generator = generator;
        _storage = storage;
        _clock = clock;
    }

    public async Task<Result<GenerateRemittanceOutcome>> GenerateAsync(
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy, CancellationToken ct)
    {
        // Load paying bank account.
        var payingAccount = await _uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == payingBankAccountId && b.CompanyId == companyId, ct);

        if (payingAccount is null)
            return Result.Failure<GenerateRemittanceOutcome>(
                Error.NotFound("Remittance.BankAccountNotFound", "Paying bank account not found."));

        if (payingAccount.BankCode != "033")
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

        // Load eligible payables: Scheduled + Open installments with DueDate <= cutoff.
        var payables = await _uow.Set<Payable>()
            .Include(p => p.Installments)
            .Where(p => p.CompanyId == companyId && p.Status == PayableStatus.Scheduled)
            .ToListAsync(ct);

        var now = _clock.GetUtcNow();
        var paymentDate = DateOnly.FromDateTime(now.UtcDateTime);

        // Compute sequence number: 1 + max existing for this paying account.
        var maxSeq = await _uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == payingBankAccountId)
            .Select(r => (int?)r.SequenceNumber)
            .MaxAsync(ct) ?? 0;
        var sequenceNumber = maxSeq + 1;

        var remittance = Remittance.Create(companyId, payingBankAccountId, payingAccount.BankCode,
            sequenceNumber, generatedBy, now);

        var skipped = new List<SkippedInstallment>();
        var fileLines = new List<RemittanceFileLine>();

        foreach (var payable in payables)
        {
            var dueInstallments = payable.Installments
                .Where(i => i.Status == InstallmentStatus.Open && i.DueDate <= dueCutoff)
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

            // Validate bank info per method.
            if (method == PaymentMethod.Ted)
            {
                if (string.IsNullOrEmpty(bankInfo.BankCode)
                    || string.IsNullOrEmpty(bankInfo.Branch)
                    || string.IsNullOrEmpty(bankInfo.Account))
                {
                    foreach (var inst in dueInstallments)
                        skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "missing vendor bank account"));
                    continue;
                }
            }
            else // Pix
            {
                if (string.IsNullOrEmpty(bankInfo.PixKey))
                {
                    foreach (var inst in dueInstallments)
                        skipped.Add(new SkippedInstallment(payable.Id, inst.Id, "missing vendor PIX key"));
                    continue;
                }
            }

            // All checks passed — add each installment.
            foreach (var inst in dueInstallments)
            {
                inst.MarkInRemittance();

                var draft = new RemittanceLineDraft(
                    PayableId: payable.Id,
                    InstallmentId: inst.Id,
                    Method: method.Value,
                    Amount: inst.Amount,
                    FavoredName: vendor.Name,
                    FavoredDocument: vendor.Document,
                    FavoredBankCode: bankInfo.BankCode,
                    FavoredAgency: bankInfo.Branch,
                    FavoredAccount: bankInfo.Account,
                    FavoredAccountDigit: bankInfo.AccountDigit,
                    FavoredPixKey: bankInfo.PixKey);

                var line = remittance.AddLine(draft);

                fileLines.Add(new RemittanceFileLine(
                    Method: method.Value,
                    Amount: inst.Amount,
                    PaymentDate: paymentDate,
                    FavoredName: vendor.Name,
                    FavoredDocument: vendor.Document,
                    FavoredBankCode: bankInfo.BankCode,
                    FavoredAgency: bankInfo.Branch,
                    FavoredAccount: bankInfo.Account,
                    FavoredAccountDigit: bankInfo.AccountDigit,
                    FavoredPixKey: bankInfo.PixKey,
                    Reference: line.Reference));
            }
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
            installment?.ReleaseFromRemittance();
        }

        remittance.Cancel();
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
