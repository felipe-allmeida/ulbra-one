using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Jobs;

/// <summary>Daily sweep: registers the baixa for due Scheduled installments at face amount → Paid.</summary>
public sealed class PayableAutoPayJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<PayableAutoPayJob> log, bool enabled, TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!enabled) return;
        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Auto-pay sweep failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var payments = scope.ServiceProvider.GetRequiredService<IPayablePaymentService>();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var due = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .Where(p => p.Status == PayableStatus.Scheduled
                && p.Installments.Any(i => i.Status != InstallmentStatus.Paid && i.DueDate <= today))
            .ToListAsync(ct);

        foreach (var payable in due)
        {
            try
            {
                var source = await ResolveSourceAsync(uow, payable, ct);
                if (source is null) { log.LogWarning("Payable {Id} due but no payment source; skipping", payable.Id); continue; }
                var (bankAccountId, method) = source.Value;
                foreach (var inst in payable.Installments.Where(i => i.Status != InstallmentStatus.Paid && i.DueDate <= today).ToList())
                {
                    var face = inst.Amount - inst.PaidAmount;
                    if (face <= 0) continue;
                    var input = new RegisterPaymentInput(today, face, bankAccountId, method, 0, 0, 0, "Pagamento automático");
                    var result = await payments.RegisterAsync(payable, inst, input, SystemActor.Id, ct);
                    if (result.IsFailure) log.LogWarning("Auto-pay {Payable}/{Inst} failed: {Err}", payable.Id, inst.Id, result.Error.Code);
                }
                await uow.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { log.LogError(ex, "Auto-pay failed for payable {Id}", payable.Id); }
        }
    }

    private static async Task<(Guid BankAccountId, PaymentMethod Method)?> ResolveSourceAsync(
        IUnitOfWork uow, Payable payable, CancellationToken ct)
    {
        // Per-payable override wins.
        if (payable.PaymentBankAccountId is { } ov && payable.PaymentMethod is { } ovm) return (ov, ovm);
        // Else the company's default paying account.
        var def = await uow.ReadSet<BankAccount>().FirstOrDefaultAsync(
            b => b.CompanyId == payable.CompanyId && b.IsDefaultForPayments && b.DeletedAt == null, ct);
        if (def is { DefaultPaymentMethod: { } m }) return (def.Id, m);
        return null;
    }
}
