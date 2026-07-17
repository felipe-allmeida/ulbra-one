using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

public interface IRemittanceSweepService
{
    Task<RemittanceSweepResult> SweepAsync(DateOnly asOf, CancellationToken ct);
}

public sealed record RemittanceSweepResult(int CompaniesProcessed, int RemittancesGenerated, int InstallmentsIncluded);

public sealed class RemittanceSweepService(
    IUnitOfWork uow,
    IRemittanceGenerationService generation,
    ILogger<RemittanceSweepService> log) : IRemittanceSweepService
{
    public async Task<RemittanceSweepResult> SweepAsync(DateOnly asOf, CancellationToken ct)
    {
        // Find candidate companies = those with a default-for-payments account that has a non-empty
        // agreement code. The Santander (033) filter is applied in-memory via BankCodes.IsSantander so
        // legacy accounts stored un-normalized ("33"/"0033") are still picked up (belt for pre-backfill data).
        var candidates = await uow.ReadSet<BankAccount>()
            .Where(b => b.IsDefaultForPayments
                        && b.DeletedAt == null
                        && b.CnabAgreementCode != null
                        && b.CnabAgreementCode != "")
            .ToListAsync(ct);

        var accounts = candidates.Where(b => BankCodes.IsSantander(b.BankCode)).ToList();

        int companies = 0, remittances = 0, lines = 0, failures = 0;

        foreach (var account in accounts)
        {
            companies++;

            // Check if any eligible installment exists (Open, not held, scheduled date arrived).
            var hasEligible = await uow.ReadSet<Payable>()
                .Where(p => p.CompanyId == account.CompanyId && p.Status == PayableStatus.Scheduled)
                .SelectMany(p => p.Installments)
                .AnyAsync(i => i.Status == InstallmentStatus.Open
                               && !i.IsHeld
                               && i.ScheduledPaymentDate <= asOf, ct);

            if (!hasEligible)
                continue;

            // The generation service filters Open + !IsHeld + ScheduledPaymentDate <= cutoff
            // and skips non-remittable installments (no payment method / unsupported / missing bank info).
            var result = await generation.GenerateAsync(
                account.CompanyId, account.Id, asOf, "system:sweep", ct);

            if (result.IsFailure)
            {
                failures++;
                log.LogWarning(
                    "Sweep: company {CompanyId} account {AccountId} generation failed: {Code} — {Description}",
                    account.CompanyId, account.Id, result.Error.Code, result.Error.Description);
                continue;
            }

            var generated = result.Value.Remittance;
            if (generated.Lines.Count > 0)
            {
                remittances++;
                lines += generated.Lines.Count;
            }
        }

        // Structured run summary (generated / skipped-companies / errors) → OTel/App Insights.
        log.LogInformation(
            "RemittanceSweep {AsOf}: {Companies} companies, {Remittances} remittances, {Installments} installments, {Failures} failures.",
            asOf, companies, remittances, lines, failures);

        return new RemittanceSweepResult(companies, remittances, lines);
    }
}
