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
        // Collect IDs in a short-lived scope; process each payable in its own scope so that
        // a failure (including the MassTransit EF outbox state) for one payable cannot
        // contaminate the DbContext / outbox state of the next.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        List<Guid> dueIds;
        using (var listScope = scopes.CreateScope())
        {
            var listUow = listScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            dueIds = await listUow.Set<Payable>()
                .Where(p => p.Status == PayableStatus.Scheduled
                    && p.Installments.Any(i => i.Status != InstallmentStatus.Paid && i.DueDate <= today))
                .Select(p => p.Id)
                .ToListAsync(ct);
        }

        foreach (var payableId in dueIds)
        {
            using var scope = scopes.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var payments = scope.ServiceProvider.GetRequiredService<IPayablePaymentService>();
            try
            {
                var payable = await uow.Set<Payable>()
                    .Include(p => p.Installments).ThenInclude(i => i.Payments)
                    .FirstOrDefaultAsync(p => p.Id == payableId, ct);
                if (payable is null) continue; // deleted between list and process
                if (payable.Status != PayableStatus.Scheduled) continue; // concurrently settled

                var source = await ResolveSourceAsync(uow, payable, ct);
                if (source is null) { log.LogWarning("Payable {Id} due but no payment source; skipping", payable.Id); continue; }
                var (bankAccountId, method) = source.Value;
                foreach (var inst in payable.Installments.Where(i => i.Status != InstallmentStatus.Paid && i.DueDate <= today).ToList())
                {
                    var face = inst.Amount - inst.PaidAmount;
                    if (face <= 0) { log.LogWarning("Auto-pay {Payable}/{Inst} face amount is {Face}; skipping", payable.Id, inst.Id, face); continue; }
                    var input = new RegisterPaymentInput(today, face, bankAccountId, method, 0, 0, 0, "Pagamento automático");
                    var result = await payments.RegisterAsync(payable, inst, input, SystemActor.Id, ct);
                    if (result.IsFailure) log.LogWarning("Auto-pay {Payable}/{Inst} failed: {Err}", payable.Id, inst.Id, result.Error.Code);
                }
                await uow.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Auto-pay failed for payable {Id}", payableId);
            }
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
