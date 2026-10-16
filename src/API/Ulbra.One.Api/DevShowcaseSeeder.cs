using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Domain;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Persistence;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Api;

/// <summary>
/// Development-only <b>additive</b> seed: fills the gaps <see cref="DevDataSeeder"/> cannot fill on a database
/// that is already populated. <c>DevDataSeeder.SeedAsync</c> is gated on <c>Companies.Count == 0</c>, so a
/// long-lived DEV database (the Azure <c>dev</c> environment, or any local one seeded before the seeder grew)
/// keeps whatever it got on its first run and never sees a later addition. This seeder is the complement: it
/// creates nothing that exists, deletes nothing that does, and adds the states the screens have no example of —
/// payables across <b>every</b> <see cref="PayableStatus"/>, a bank reconciliation exercising every
/// <see cref="MatchStatus"/> and every <see cref="Ulbra.One.Finance.Application.Conciliation.ConciliationMismatch"/>
/// case, CNAB remittances in all four <see cref="RemittanceStatus"/> values with their returns, receivables in
/// all four <see cref="ReceivableStatus"/> values, accounting periods/journal entries, and budgeting projects
/// and adjustments.
///
/// <para><b>Idempotent by section.</b> Every section is guarded by a natural marker (<see cref="Tag"/> in a
/// document number, file name or source reference), so re-running is a no-op and a partially applied run
/// resumes where it stopped. It runs on every Development startup — including the Azure <c>dev</c>
/// environment, whose container app sets <c>ASPNETCORE_ENVIRONMENT=Development</c>.</para>
///
/// <para><b>Adapts to the database it finds.</b> Nothing here hardcodes a company, cost center or vendor name:
/// the target org is resolved by querying what exists (see <see cref="ResolveAsync"/>), because DEV's org
/// structure has drifted from what <see cref="DevDataSeeder"/> would create today. When the database has no
/// usable company (no bank account, cost center, unit or vendor), the whole seed is skipped rather than
/// inventing an org — that is <see cref="DevDataSeeder.SeedAsync"/>'s job on an empty database.</para>
/// </summary>
public static class DevShowcaseSeeder
{
    /// <summary>Marker stamped on everything this seeder creates — the per-section idempotency key, and what
    /// tells a human (and a cleanup script) which rows are showcase data rather than real DEV usage.</summary>
    public const string Tag = "SHW";

    /// <summary>Runs every section, each isolated. Never throws: a showcase seed must not be able to crash the
    /// API at startup, so a failure is logged and startup continues (mirrors the production foundation seed).
    /// <para>Sections are isolated from each other on purpose — one that fails must not silently take the ones
    /// after it down with it, and a fresh <see cref="AppDbContext"/> per section keeps a failed section's
    /// half-built change tracker from poisoning the next <c>SaveChanges</c>.</para></summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var time = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevShowcaseSeeder");

        Ctx? ctx;
        try
        {
            using var probe = services.CreateScope();
            ctx = await ResolveAsync(probe.ServiceProvider.GetRequiredService<AppDbContext>(), time, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Showcase seed: could not resolve a target company; skipping.");
            return;
        }

        if (ctx is null)
        {
            logger.LogInformation(
                "Showcase seed: no company with a bank account + cost center + unit + vendor — skipping "
                + "(the base seed owns an empty database).");
            return;
        }

        var sections = new (string Name, Func<AppDbContext, Ctx, ILogger, CancellationToken, Task> Run)[]
        {
            ("payables", SeedPayablesAsync),
            ("conciliation", SeedConciliationAsync),
            ("remittances", SeedRemittancesAsync),
            ("receivables", SeedReceivablesAsync),
            ("accounting", SeedAccountingAsync),
            ("budgeting", SeedBudgetingAsync),
        };

        foreach (var (name, run) in sections)
        {
            try
            {
                using var sectionScope = services.CreateScope();
                await run(sectionScope.ServiceProvider.GetRequiredService<AppDbContext>(), ctx, logger, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Showcase seed: section '{Section}' failed; continuing with the rest.", name);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Context
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Everything the sections need, resolved from the database as it actually is.</summary>
    private sealed record Ctx(
        Guid CompanyId,
        string CompanyName,
        IReadOnlyList<Unit> Units,
        IReadOnlyList<CostCenter> CostCenters,
        IReadOnlyList<VendorEntity> Vendors,
        IReadOnlyList<CustomerEntity> Customers,
        BankAccount Bank,
        IReadOnlyList<ExpenseCategory> Categories,
        IReadOnlyList<LedgerAccount> ExpenseAccounts,
        IReadOnlyList<LedgerAccount> CashAccounts,
        Guid ActorId,
        DateTimeOffset Now,
        DateOnly Today)
    {
        /// <summary>Deterministic round-robin pickers, so a section can spread its rows over whatever the
        /// database happens to have without caring how many there are.</summary>
        public CostCenter Cc(int i) => CostCenters[i % CostCenters.Count];
        public Unit Un(int i) => Units[i % Units.Count];
        public VendorEntity Vd(int i) => Vendors[i % Vendors.Count];
        public CustomerEntity Cu(int i) => Customers[i % Customers.Count];
        public Guid? Cat(int i) => Categories.Count == 0 ? null : Categories[i % Categories.Count].Id;
        public Guid? Expense(int i) => ExpenseAccounts.Count == 0 ? null : ExpenseAccounts[i % ExpenseAccounts.Count].Id;
    }

    /// <summary>Picks the company to seed into: the one that can actually carry the data (bank account, cost
    /// center, unit) and already holds the most payables — i.e. the one a developer is looking at. Returns null
    /// when no company qualifies.</summary>
    private static async Task<Ctx?> ResolveAsync(AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var vendors = await db.Set<VendorEntity>().OrderBy(v => v.Name).Take(24).ToListAsync(ct);
        if (vendors.Count == 0) return null;

        var companies = await db.Set<Company>().OrderBy(c => c.LegalName).ToListAsync(ct);
        foreach (var company in companies
            .OrderByDescending(c => db.Set<Payable>().Count(p => p.CompanyId == c.Id))
            .ThenBy(c => c.LegalName))
        {
            var bank = await db.Set<BankAccount>()
                .Where(b => b.CompanyId == company.Id)
                .OrderBy(b => b.Alias)
                .FirstOrDefaultAsync(ct);
            if (bank is null) continue;

            var costCenters = await db.Set<CostCenter>()
                .Where(c => c.CompanyId == company.Id)
                .OrderBy(c => c.Name)
                .ToListAsync(ct);
            if (costCenters.Count == 0) continue;

            var units = await db.Set<Unit>()
                .Where(u => u.CompanyId == company.Id)
                .OrderBy(u => u.Name)
                .ToListAsync(ct);
            if (units.Count == 0) continue;

            var customers = await db.Set<CustomerEntity>()
                .Where(c => c.CompanyId == company.Id)
                .OrderBy(c => c.Name)
                .ToListAsync(ct);
            if (customers.Count == 0) continue;

            var categories = await db.Set<ExpenseCategory>().OrderBy(c => c.Name).ToListAsync(ct);

            // Analytical expense accounts classify the payables; analytical cash accounts are the credit side of
            // the journal entries. Both come from the AELBRA chart the base seed guarantees.
            var accounts = await db.Set<LedgerAccount>()
                .Where(a => a.Kind == LedgerAccountKind.Analytical)
                .OrderBy(a => a.Code)
                .ToListAsync(ct);
            var expenses = accounts.Where(a => a.AccountGroup == AccountGroup.Expense).ToList();
            var cash = accounts.Where(a => a.AccountGroup == AccountGroup.Asset).ToList();
            if (expenses.Count == 0 || cash.Count == 0) continue;

            var actor = await db.Set<User>()
                .Where(u => u.Role == Roles.Admin)
                .OrderBy(u => u.Email)
                .Select(u => u.Id)
                .FirstOrDefaultAsync(ct);

            var now = time.GetUtcNow();
            return new Ctx(company.Id, company.LegalName, units, costCenters, vendors, customers, bank,
                categories, expenses, cash, actor, now, DateOnly.FromDateTime(now.UtcDateTime));
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------------
    // Payables — one example of every PayableStatus
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Creates a payable per <see cref="PayableStatus"/>, driving each one through the real domain
    /// transitions rather than assigning the status directly. Guarded by the <see cref="Tag"/> document-number
    /// prefix.</summary>
    private static async Task SeedPayablesAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string prefix = Tag + "-AP-";
        if (await db.Set<Payable>().AnyAsync(p => p.DocumentNumber != null && p.DocumentNumber.StartsWith(prefix), ct))
        {
            logger.LogInformation("Showcase seed: payables already present — skipping.");
            return;
        }

        var seq = 0;
        var created = new List<Payable>();

        Payable New(string description, int vendorIx, decimal total, int dueInDays, int installments = 1)
        {
            var vendor = c.Vd(vendorIx);
            var rows = Payable.GenerateInstallments(total, installments, c.Today.AddDays(dueInDays), 1);
            var payable = new Payable(c.CompanyId, PayableOrigin.Manual, vendor.Id, vendor.Name,
                $"{prefix}{++seq:0000}", c.Today.AddDays(-3), description, c.Cc(seq).Id, c.Cat(seq), rows,
                c.Un(seq).Id);
            created.Add(payable);
            return payable;
        }

        // --- Received: raw intake, nothing done to it yet.
        New("Material de expediente - reposição trimestral", 0, 1_240m, 12);
        New("Manutenção predial - bloco B", 1, 3_480m, 20, installments: 2);

        // --- PendingClassification: intake triaged, waiting for the Contábil to pick a ledger account.
        New("Serviço de vigilância patrimonial - mensal", 2, 9_750m, 8).MarkPendingClassification();
        New("Licença de software acadêmico (renovação)", 3, 4_100m, 25).MarkPendingClassification();

        // --- PendingApproval: classified and typed, waiting on the approver.
        var pa1 = New("Consultoria de acessibilidade - laudo NBR 9050", 4, 6_300m, 5);
        pa1.Classify(c.Expense(0));
        pa1.SetType(PayableType.Service);
        pa1.MarkPendingApproval();

        var pa2 = New("Reforma da quadra poliesportiva", 5, 28_500m, 18, installments: 3);
        pa2.Classify(c.Expense(1));
        pa2.SetType(PayableType.Product);
        pa2.MarkPendingApproval();

        // --- PendingTaxAnalysis: a Serviço title parks here on approval until the Tributário validates the
        // retenções. Withholdings are set first (they lock once the title is released to Scheduled).
        var tax1 = New("Assessoria jurídica trabalhista", 6, 15_900m, 10);
        tax1.Classify(c.Expense(2));
        tax1.SetType(PayableType.Service);
        tax1.RequireTaxAnalysis();
        tax1.SetWithholdings(new[]
        {
            (WithholdingTaxType.Irrf, 15_900m, (decimal?)0.015m, 238.50m),
            (WithholdingTaxType.Iss, 15_900m, (decimal?)0.05m, 795m),
        });
        tax1.Approve("system", c.Now);

        var tax2 = New("Manutenção de elevadores - contrato anual", 7, 7_420m, 15);
        tax2.Classify(c.Expense(3));
        tax2.SetType(PayableType.Service);
        tax2.RequireTaxAnalysis();
        tax2.SetWithholdings(new[]
        {
            (WithholdingTaxType.Inss, 7_420m, (decimal?)0.11m, 816.20m),
        });
        tax2.Approve("system", c.Now);

        // --- PendingAccounting: approved, waiting on the accounting review (one of them already overdue).
        var acc1 = New("Aquisição de mobiliário para laboratório", 8, 11_200m, 22);
        acc1.Classify(c.Expense(4));
        acc1.SetType(PayableType.Product);
        acc1.Approve("system", c.Now);

        var acc2 = New("Transporte de equipamentos entre campi", 9, 2_870m, -6);
        acc2.Classify(c.Expense(5));
        acc2.SetType(PayableType.Service);
        acc2.Approve("system", c.Now);

        // --- Scheduled: released for payment. One overdue, one parcelado, one due today.
        Payable Scheduled(string description, int vendorIx, decimal total, int dueInDays, int installments = 1)
        {
            var p = New(description, vendorIx, total, dueInDays, installments);
            p.Classify(c.Expense(vendorIx));
            p.SetType(PayableType.Product);
            p.SetPaymentSource(c.Bank.Id, PaymentMethod.Ted);
            p.Approve("system", c.Now);
            p.CompleteAccounting("system", c.Now, hasFiscalDocument: false, allowWithoutFiscalDocument: true);
            return p;
        }

        Scheduled("Fornecimento de gás - cozinha do RU", 10, 5_310m, -9);
        Scheduled("Assinatura de periódicos científicos", 11, 18_600m, 30, installments: 4);
        Scheduled("Insumos de laboratório de química", 12, 3_960m, 0);

        // --- Scheduled + partially paid: the parcela carries a payment that does not settle it.
        var partial = Scheduled("Locação de impressoras - trimestre", 13, 9_000m, 4);
        var partialInst = partial.Installments[0];
        partialInst.RegisterPayment(new Payment(partialInst.Id, partial.Id, c.Today.AddDays(-2), 3_500m,
            c.Bank.Id, PaymentMethod.Pix, 0, 0, 0, "Pagamento parcial acordado", c.ActorId, c.Now));
        partial.RecomputeSettlement();

        // --- Paid: every parcela settled.
        var paid1 = Scheduled("Serviço de dedetização - campus", 14, 2_150m, -12);
        SettleInFull(paid1, c, PaymentMethod.Boleto, c.Today.AddDays(-12));

        var paid2 = Scheduled("Equipamentos de rede - switches", 15, 12_400m, -20, installments: 2);
        SettleInFull(paid2, c, PaymentMethod.Ted, c.Today.AddDays(-18));

        // --- Rejected and Cancelled: the two terminal states that never reach payment.
        New("Aquisição de veículo utilitário", 16, 96_000m, 40).Reject(
            "Nota fiscal divergente do pedido de compra", "system", c.Now);
        New("Serviço de tradução juramentada", 17, 1_780m, 9).Cancel(
            "Contrato cancelado pelo fornecedor");

        await db.Set<Payable>().AddRangeAsync(created, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Showcase seed: {Count} payables seeded across every PayableStatus.", created.Count);
    }

    /// <summary>Settles every installment of a scheduled payable and re-derives its status (→ Paid).</summary>
    private static void SettleInFull(Payable payable, Ctx c, PaymentMethod method, DateOnly paidOn)
    {
        foreach (var installment in payable.Installments)
        {
            var outstanding = installment.NetAmount - installment.PaidAmount;
            if (outstanding <= 0) continue;
            installment.RegisterPayment(new Payment(installment.Id, payable.Id, paidOn, outstanding,
                c.Bank.Id, method, 0, 0, 0, null, c.ActorId, c.Now));
        }
        payable.RecomputeSettlement();
    }

    // ---------------------------------------------------------------------------------------------------
    // Conciliation — every MatchStatus and every ConciliationMismatch the screen can explain
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Seeds two statement imports whose lines are engineered against
    /// <see cref="Ulbra.One.Finance.Application.Conciliation.ConciliationMatcher"/>'s real thresholds, so the
    /// reconciliation screen shows one row per verdict instead of a wall of "não conciliado".
    /// <para>
    /// The matcher's pool is the open/partially-paid installments of <b>Scheduled or Paid</b> payables due within
    /// ±30 days of the statement line, an exact-value match inside a ±5-day window, and a near-miss tolerance of
    /// <c>max(R$ 50, 2%)</c>. Each fixture below picks its value and due date to land on exactly one verdict:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Reconciled</b> — debit tied to a payment the conciliation itself registered.</item>
    ///   <item><b>Suggested</b> — single exact match inside the window; waiting for a human to confirm.</item>
    ///   <item><b>Ignored</b> — an internal transfer, deliberately taken off the queue.</item>
    ///   <item><b>ExactAvailable</b> — one exact match one click away (the import-time matcher is greedy and
    ///     missed it).</item>
    ///   <item><b>AmountDiffers</b> — right day, value off by juros inside the tolerance band.</item>
    ///   <item><b>OutsideDateWindow</b> — exact value, but 12 days from the statement day.</item>
    ///   <item><b>Ambiguous</b> — two identical títulos; the matcher refuses to guess.</item>
    ///   <item><b>NoCandidate</b> — a bank fee, which has no título behind it.</item>
    ///   <item><b>CreditNotMatched</b> — a credit; automatic matching only covers debits.</item>
    /// </list>
    /// Guarded by the import file-name prefix.
    /// </summary>
    private static async Task SeedConciliationAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string filePrefix = "showcase-";
        if (await db.Set<BankStatementImport>().AnyAsync(i => i.FileName.StartsWith(filePrefix), ct))
        {
            logger.LogInformation("Showcase seed: statement imports already present — skipping conciliation.");
            return;
        }

        var seq = 0;
        var fixtures = new List<Payable>();

        // A scheduled payable with a single open installment of exactly `total`, due `dueInDays` from today —
        // the raw material the matcher works against. No withholdings: they would move NetAmount, and the pool
        // matches on the remaining NET balance.
        Payable Fixture(string description, int vendorIx, decimal total, int dueInDays)
        {
            var vendor = c.Vd(vendorIx);
            var rows = Payable.GenerateInstallments(total, 1, c.Today.AddDays(dueInDays), 1);
            var p = new Payable(c.CompanyId, PayableOrigin.Manual, vendor.Id, vendor.Name,
                $"{Tag}-CC-{++seq:0000}", c.Today.AddDays(-5), description, c.Cc(seq).Id, c.Cat(seq), rows,
                c.Un(seq).Id);
            p.Classify(c.Expense(seq));
            p.SetType(PayableType.Product);
            p.SetPaymentSource(c.Bank.Id, PaymentMethod.Ted);
            p.Approve("system", c.Now);
            p.CompleteAccounting("system", c.Now, hasFiscalDocument: false, allowWithoutFiscalDocument: true);
            fixtures.Add(p);
            return p;
        }

        var exact = Fixture("Papelaria e suprimentos de impressão", 0, 5_980m, 2);       // → ExactAvailable
        Fixture("Água e esgoto - campus", 1, 4_280m, 1);                                // → AmountDiffers
        var faraway = Fixture("Seguro patrimonial - parcela", 2, 7_640m, 12);            // → OutsideDateWindow
        var twinA = Fixture("Serviço de limpeza - bloco A", 3, 3_150m, 3);               // ┐ → Ambiguous
        var twinB = Fixture("Serviço de limpeza - bloco C", 4, 3_150m, 3);               // ┘
        var suggested = Fixture("Telefonia e links de dados", 5, 2_415m, 0);             // → Suggested
        var settled = Fixture("Combustível da frota", 6, 8_900m, -2);                    // → Reconciled

        // The Reconciled line points at a real payment, so the payable settles like any other baixa.
        SettleInFull(settled, c, PaymentMethod.Pix, c.Today.AddDays(-2));
        var settledPayment = settled.Installments[0].Payments[0];

        await db.Set<Payable>().AddRangeAsync(fixtures, ct);

        var period = (Start: c.Today.AddDays(-15), End: c.Today.AddDays(1));
        var ofx = new BankStatementImport(c.CompanyId, c.Bank.Id, StatementSource.OfxUpload,
            $"{filePrefix}extrato-{c.Today:yyyy-MM}.ofx", period.Start, period.End, 7, 7, 0);
        var csv = new BankStatementImport(c.CompanyId, c.Bank.Id, StatementSource.CsvUpload,
            $"{filePrefix}extrato-conciliado-{c.Today:yyyy-MM}.csv", period.Start, period.End, 2, 2, 0);

        var txns = new List<BankTransaction>();
        var n = 0;
        BankTransaction Line(BankStatementImport import, decimal amount, string description, int dayOffset = 0)
        {
            var t = new BankTransaction(import.Id, c.CompanyId, c.Bank.Id, $"{Tag}-TXN-{++n:0000}",
                c.Today.AddDays(dayOffset), amount, description, c.Now);
            txns.Add(t);
            return t;
        }

        // --- Still open: one line per unmatched verdict.
        Line(ofx, -5_980m, "PAGTO FORNECEDOR PAPELARIA");                                 // ExactAvailable
        Line(ofx, -4_312.80m, "PAGTO CONCESSIONARIA AGUA");                               // AmountDiffers (juros)
        Line(ofx, -7_640m, "PAGTO SEGURADORA APOLICE");                                   // OutsideDateWindow
        Line(ofx, -3_150m, "PAGTO SERVICOS LIMPEZA");                                     // Ambiguous
        Line(ofx, -63.90m, "TAR PACOTE DE SERVICOS");                                     // NoCandidate
        Line(ofx, 12_480m, "TED RECEBIDA CONVENIO ESTAGIO");                              // CreditNotMatched
        Line(ofx, -2_415m, "PAGTO TELECOM").Suggest(suggested.Installments[0].Id);        // Suggested

        // --- Already treated: the two states a human (or a previous conciliation) produced.
        Line(csv, -8_900m, "PAGTO POSTO COMBUSTIVEL", -2).Reconcile(settledPayment.Id);   // Reconciled
        Line(csv, -1_500m, "TRANSF ENTRE CONTAS PROPRIAS", -1).Ignore();                  // Ignored

        await db.Set<BankStatementImport>().AddRangeAsync(new[] { ofx, csv }, ct);
        await db.Set<BankTransaction>().AddRangeAsync(txns, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Showcase seed: 2 statement imports + {Lines} bank transactions seeded (every MatchStatus and every "
            + "ConciliationMismatch), backed by {Fixtures} scheduled payables. Twins {TwinA}/{TwinB} drive the "
            + "ambiguity; {Exact} is the one-click match; {Far} is the out-of-window one.",
            txns.Count, fixtures.Count, twinA.DocumentNumber, twinB.DocumentNumber,
            exact.DocumentNumber, faraway.DocumentNumber);
    }

    // ---------------------------------------------------------------------------------------------------
    // Remittances — all four RemittanceStatus values, their line states, and the returns behind them
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Seeds one CNAB remittance per <see cref="RemittanceStatus"/>, each in a different
    /// <see cref="RemittanceCategory"/>, plus the two <see cref="RemittanceReturn"/> rows whose processing
    /// produced the resolved ones. Statuses are reached through <see cref="Remittance.Recompute"/> from the
    /// line states — never assigned — so the aggregate stays internally consistent:
    /// <list type="bullet">
    ///   <item><b>Generated</b> (PIX) — lines Pending, installments locked into the remittance.</item>
    ///   <item><b>Confirmed</b> (TED) — every line Paid, each with the baixa the return registered.</item>
    ///   <item><b>PartiallyReturned</b> (boleto) — one line Paid, one Returned and released back to Open.</item>
    ///   <item><b>Cancelled</b> (convênio) — cancelled before sending; the installment goes back to Open.</item>
    /// </list>
    /// Guarded by the remittance file-name prefix.
    /// </summary>
    private static async Task SeedRemittancesAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string filePrefix = "showcase-remessa-";
        if (await db.Set<Remittance>().AnyAsync(r => r.FileName != null && r.FileName.StartsWith(filePrefix), ct))
        {
            logger.LogInformation("Showcase seed: remittances already present — skipping.");
            return;
        }

        var nextSequence = 1 + await db.Set<Remittance>()
            .Where(r => r.CompanyId == c.CompanyId)
            .Select(r => (int?)r.SequenceNumber)
            .MaxAsync(ct) ?? 1;

        var seq = 0;
        var payables = new List<Payable>();

        // Payables that ride the remittance rail. Kept separate from the conciliation fixtures: an installment
        // in a remittance leaves the conciliation pool, which would silently change those verdicts.
        Payable Payable(string description, int vendorIx, decimal total, int dueInDays, PaymentMethod method)
        {
            var vendor = c.Vd(vendorIx);
            var rows = Ulbra.One.Finance.Domain.Entities.Payable.GenerateInstallments(
                total, 1, c.Today.AddDays(dueInDays), 1);
            var p = new Payable(c.CompanyId, PayableOrigin.Manual, vendor.Id, vendor.Name,
                $"{Tag}-RM-{++seq:0000}", c.Today.AddDays(-8), description, c.Cc(seq).Id, c.Cat(seq), rows,
                c.Un(seq).Id);
            p.Classify(c.Expense(seq));
            p.SetType(PayableType.Service);
            p.SetPaymentSource(c.Bank.Id, method);
            p.Approve("system", c.Now);
            p.CompleteAccounting("system", c.Now, hasFiscalDocument: false, allowWithoutFiscalDocument: true);
            payables.Add(p);
            return p;
        }

        var remittances = new List<Remittance>();
        Remittance Remittance(RemittanceCategory category, int dayOffset)
        {
            var r = Ulbra.One.Finance.Domain.Entities.Remittance.Create(c.CompanyId, c.Bank.Id, c.Bank.BankCode,
                nextSequence++, "system", c.Now, category, c.Today.AddDays(dayOffset));
            r.SetFile($"remittances/{r.Id}.rem", $"{filePrefix}{r.SequenceNumber:000}.rem", 2_400);
            remittances.Add(r);
            return r;
        }

        // Adds one line for the payable's single installment and locks it into the file.
        RemittanceLine Add(Remittance remittance, Payable payable, PaymentMethod method)
        {
            var installment = payable.Installments[0];
            var vendor = c.Vendors.First(v => v.Id == payable.VendorId);
            installment.MarkInRemittance();
            return remittance.AddLine(new RemittanceLineDraft(
                payable.Id, installment.Id, method, installment.NetAmount - installment.PaidAmount,
                vendor.Name, vendor.Document, c.Bank.BankCode, "0001", "000123456", "7",
                $"{vendor.Document}@showcase.dev", installment.DueDate));
        }

        // --- Generated: waiting for the bank to answer.
        var generated = Remittance(RemittanceCategory.Pix, 1);
        Add(generated, Payable("Serviço de portaria - dezembro", 0, 6_450m, 1, PaymentMethod.Pix), PaymentMethod.Pix);
        Add(generated, Payable("Manutenção de ar-condicionado", 1, 2_980m, 2, PaymentMethod.Pix), PaymentMethod.Pix);
        generated.Recompute();

        // --- Confirmed: the bank settled every line; each one carries its baixa.
        var confirmed = Remittance(RemittanceCategory.Ted, -4);
        var okA = Payable("Serviço de jardinagem - trimestre", 2, 4_700m, -4, PaymentMethod.Ted);
        var okB = Payable("Assessoria de imprensa", 3, 3_300m, -4, PaymentMethod.Ted);
        foreach (var (payable, line) in new[]
        {
            (okA, Add(confirmed, okA, PaymentMethod.Ted)),
            (okB, Add(confirmed, okB, PaymentMethod.Ted)),
        })
        {
            line.MarkPaid(c.Today.AddDays(-3), $"BR{line.Sequence:000}9911", "AUT-SHW");
            SettleInFull(payable, c, PaymentMethod.Ted, c.Today.AddDays(-3));
        }
        confirmed.Recompute();

        // --- PartiallyReturned: one paid, one refused by the bank and back in the queue.
        var partial = Remittance(RemittanceCategory.BoletoBancario, -6);
        var settledOne = Payable("Fornecimento de material gráfico", 4, 5_120m, -6, PaymentMethod.Boleto);
        var refused = Payable("Locação de tendas para formatura", 5, 8_250m, -6, PaymentMethod.Boleto);
        var settledLine = Add(partial, settledOne, PaymentMethod.Boleto);
        var refusedLine = Add(partial, refused, PaymentMethod.Boleto);
        settledLine.MarkPaid(c.Today.AddDays(-5), "BR0019955", "AUT-SHW");
        SettleInFull(settledOne, c, PaymentMethod.Boleto, c.Today.AddDays(-5));
        refusedLine.MarkReturned("Conta do favorecido encerrada (ocorrência AG)");
        refused.Installments[0].ReleaseFromRemittance();
        partial.Recompute();

        // --- Cancelled: pulled before it was sent, so the título returns to the payment queue.
        var cancelled = Remittance(RemittanceCategory.Convenio, 0);
        var pulled = Payable("Guia de recolhimento - taxa municipal", 6, 1_890m, 3, PaymentMethod.Boleto);
        Add(cancelled, pulled, PaymentMethod.Boleto);
        cancelled.Cancel();
        pulled.Installments[0].ReleaseFromRemittance();

        // The two returns whose application produced the resolved remittances above.
        var returns = new[]
        {
            RemittanceReturn.Create(c.CompanyId, c.Bank.Id, $"returns/{Tag}-ret-001.ret",
                $"showcase-retorno-{c.Today.AddDays(-3):yyyyMMdd}.ret",
                matchedCount: 2, paidCount: 2, returnedCount: 0, unmatchedCount: 0, divergentCount: 0,
                "system", c.Now),
            RemittanceReturn.Create(c.CompanyId, c.Bank.Id, $"returns/{Tag}-ret-002.ret",
                $"showcase-retorno-{c.Today.AddDays(-5):yyyyMMdd}.ret",
                matchedCount: 2, paidCount: 1, returnedCount: 1, unmatchedCount: 1, divergentCount: 1,
                "system", c.Now),
        };

        await db.Set<Payable>().AddRangeAsync(payables, ct);
        await db.Set<Remittance>().AddRangeAsync(remittances, ct);
        await db.Set<RemittanceReturn>().AddRangeAsync(returns, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Showcase seed: {Remittances} remittances seeded (Generated/Confirmed/PartiallyReturned/Cancelled) "
            + "over {Payables} payables, plus {Returns} processed returns.",
            remittances.Count, payables.Count, returns.Length);
    }

    // ---------------------------------------------------------------------------------------------------
    // Receivables — all four ReceivableStatus values
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Seeds a receivable per <see cref="ReceivableStatus"/> — the base seed only ever produces
    /// <see cref="ReceivableStatus.AwaitingReceipt"/> — plus one partially received parcela. Guarded by the
    /// <see cref="Tag"/> document-number prefix.</summary>
    private static async Task SeedReceivablesAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string prefix = Tag + "-AR-";
        if (await db.Set<Receivable>().AnyAsync(r => r.DocumentNumber != null && r.DocumentNumber.StartsWith(prefix), ct))
        {
            logger.LogInformation("Showcase seed: receivables already present — skipping.");
            return;
        }

        var seq = 0;
        var created = new List<Receivable>();

        Receivable New(string description, int customerIx, decimal total, int count, int firstDueInDays)
        {
            var customer = c.Cu(customerIx);
            var rows = Receivable.GenerateInstallments(total, count, c.Today.AddDays(firstDueInDays), 1);
            var r = new Receivable(c.CompanyId, ReceivableOrigin.Manual, customer.Id, customer.Name,
                $"{prefix}{++seq:0000}", c.Today.AddDays(-10), description, c.Cc(seq).Id, null, null, rows);
            created.Add(r);
            return r;
        }

        void Receive(Receivable receivable, ReceivableInstallment installment, decimal amount, DateOnly on)
        {
            installment.RegisterReceipt(new Receipt(installment.Id, receivable.Id, on, amount, c.Bank.Id,
                PaymentMethod.Pix, 0, 0, 0, null, c.ActorId, c.Now));
        }

        // --- AwaitingReceipt with one parcela partially received.
        var partial = New("Contrato de cessão de espaço - cantina", 0, 12_000m, 3, -5);
        Receive(partial, partial.Installments[0], 2_000m, c.Today.AddDays(-4));
        partial.RecomputeSettlement();

        // --- Received: every parcela settled.
        var received = New("Patrocínio de evento acadêmico", 1, 9_600m, 2, -20);
        foreach (var installment in received.Installments)
            Receive(received, installment, installment.Amount, c.Today.AddDays(-18));
        received.RecomputeSettlement();

        // --- Cancelled: no receipts, so the domain allows the cancellation.
        New("Convênio de pesquisa - aditivo não assinado", 2, 45_000m, 6, 10)
            .Cancel(c.ActorId, c.Now, "Aditivo não assinado pela contraparte");

        // --- WrittenOff: uncollectible, written off after the aging ran out.
        New("Mensalidades inadimplentes - exercício anterior", 3, 7_300m, 2, -180)
            .WriteOff("Inadimplência superior a 180 dias — baixa contábil");

        await db.Set<Receivable>().AddRangeAsync(created, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Showcase seed: {Count} receivables seeded across every ReceivableStatus.", created.Count);
    }

    // ---------------------------------------------------------------------------------------------------
    // Accounting — periods, every JournalStatus, and the parked-postings queue
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Seeds an open and a closed <see cref="AccountingPeriod"/>, journal entries in all three
    /// <see cref="JournalStatus"/> values (the base seed only posts), and two unresolved
    /// <see cref="PendingAccountingPosting"/> rows so the "postings parked" queue is not empty. Entry numbers
    /// continue the company's existing sequence. Guarded by the <see cref="Tag"/> source reference.</summary>
    private static async Task SeedAccountingAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string sourceRef = Tag + "-JOURNAL";
        if (await db.Set<JournalEntry>().AnyAsync(e => e.SourceReference == sourceRef, ct))
        {
            logger.LogInformation("Showcase seed: journal entries already present — skipping accounting.");
            return;
        }

        // --- Periods: the current month open, the previous one closed (so the closed-period guard has a subject).
        var currentMonth = new DateOnly(c.Today.Year, c.Today.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);
        foreach (var (month, close) in new[] { (currentMonth, false), (previousMonth, true) })
        {
            var exists = await db.Set<AccountingPeriod>()
                .AnyAsync(p => p.CompanyId == c.CompanyId && p.FiscalYear == month.Year && p.Month == month.Month, ct);
            if (exists) continue;
            var period = new AccountingPeriod(c.CompanyId, month.Year, month.Month);
            if (close) { period.StartClosing(); period.Close(); }
            await db.Set<AccountingPeriod>().AddAsync(period, ct);
        }

        var nextNumber = 1 + await db.Set<JournalEntry>()
            .Where(e => e.CompanyId == c.CompanyId)
            .Select(e => (int?)e.Number)
            .MaxAsync(ct) ?? 1;

        var expense = c.ExpenseAccounts[0];
        var otherExpense = c.ExpenseAccounts[Math.Min(1, c.ExpenseAccounts.Count - 1)];
        var cash = c.CashAccounts[0];
        var entries = new List<JournalEntry>();

        JournalEntry Entry(string description, LedgerAccount debit, decimal amount, DateOnly when)
        {
            var entry = new JournalEntry(c.CompanyId, when, description, JournalOrigin.Manual, sourceRef);
            entry.AddLine(new JournalLine(debit.Id, EntrySide.Debit, amount, c.Cc(0).Id, c.Un(0).Id));
            entry.AddLine(new JournalLine(cash.Id, EntrySide.Credit, amount, c.Cc(0).Id, c.Un(0).Id));
            entries.Add(entry);
            return entry;
        }

        // --- Draft: balanced but never posted, so the Livro Diário has something to post.
        Entry("Rateio de despesas administrativas (rascunho)", expense, 4_500m, currentMonth.AddDays(9));

        // --- Posted.
        Entry("Despesa de manutenção predial", expense, 12_800m, currentMonth.AddDays(3))
            .Post(nextNumber++, c.Now);
        Entry("Serviços de terceiros - competência", otherExpense, 6_150m, currentMonth.AddDays(6))
            .Post(nextNumber++, c.Now);

        // --- Reversed: a posted entry plus the reversal that froze it (Reverse builds and posts the mirror).
        var mistaken = Entry("Lançamento com centro de custo incorreto", otherExpense, 3_400m, currentMonth.AddDays(11));
        mistaken.Post(nextNumber++, c.Now);
        var reversal = mistaken.Reverse("Centro de custo incorreto", currentMonth.AddDays(12), nextNumber++, c.Now);
        entries.Add(reversal);

        await db.Set<JournalEntry>().AddRangeAsync(entries, ct);

        // --- Parked postings: the incurrals the automatic posting could not resolve.
        var pending = new[]
        {
            new PendingAccountingPosting(c.CompanyId, $"{Tag}:payable:missing-account", PostingKind.Incurral,
                9_750m, "Conta contábil de despesa não mapeada para a categoria do título.",
                "4.2.15.10.200 / 2.1.05.05.010"),
            new PendingAccountingPosting(c.CompanyId, $"{Tag}:payment:closed-period", PostingKind.Settlement,
                5_120m, "Período contábil fechado na data da baixa.",
                "2.1.05.05.010 / 1.2.05.15.015"),
        };
        foreach (var posting in pending) posting.Stamp(c.Now);
        await db.Set<PendingAccountingPosting>().AddRangeAsync(pending, ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Showcase seed: {Entries} journal entries seeded (Draft/Posted/Reversed) + {Pending} parked postings "
            + "+ open/closed accounting periods.", entries.Count, pending.Length);
    }

    // ---------------------------------------------------------------------------------------------------
    // Budgeting — projects and budget adjustments across their lifecycles
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Seeds a <see cref="Project"/> per <see cref="ProjectStatus"/> and a
    /// <see cref="BudgetAdjustment"/> per <see cref="BudgetAdjustmentStatus"/>. An approved supplementation also
    /// moves the budget cell — the endpoint's job in production — so the ceiling agrees with the adjustment;
    /// the reversed one nets to zero and leaves the cell alone. Guarded by the <see cref="Tag"/> name prefix.</summary>
    private static async Task SeedBudgetingAsync(AppDbContext db, Ctx c, ILogger logger, CancellationToken ct)
    {
        const string namePrefix = "[" + Tag + "] ";
        if (await db.Set<Project>().AnyAsync(p => p.Name.StartsWith(namePrefix), ct))
        {
            logger.LogInformation("Showcase seed: budgeting showcase already present — skipping.");
            return;
        }

        // --- Projects: one per lifecycle state.
        var projects = new[]
        {
            new Project(c.CompanyId, c.Un(0).Id, $"{namePrefix}Modernização dos laboratórios", 480_000m, 126_400m,
                "Troca de bancadas e equipamentos dos laboratórios de graduação."),
            new Project(c.CompanyId, c.Un(1).Id, $"{namePrefix}Recredenciamento EAD", 210_000m, 210_000m,
                "Adequação de polos e plataforma para o recredenciamento."),
            new Project(c.CompanyId, c.Un(2).Id, $"{namePrefix}Nova biblioteca setorial", 1_250_000m, 38_000m,
                "Suspenso: obra não aprovada no orçamento do exercício."),
        };
        projects[1].Finish();
        projects[2].Cancel();
        await db.Set<Project>().AddRangeAsync(projects, ct);

        // --- Budget adjustments: one per status, on a competency the budget actually covers.
        var nextCode = 1 + await db.Set<BudgetAdjustment>()
            .Where(a => a.CompanyId == c.CompanyId)
            .Select(a => (int?)a.Code)
            .MaxAsync(ct) ?? 1;

        var competency = Competency.FromDate(c.Now);
        var unit = c.Un(0);
        var costCenter = c.Cc(0);

        // Counterparts (entregas) are mandatory for every type except a Redução — the domain enforces it, since
        // asking for more budget has to be justified by what it delivers.
        BudgetAdjustment Adjustment(BudgetAdjustmentType type, decimal value, string justification,
            string? counterparts, string? sourceCompetency = null) =>
            new(c.CompanyId, unit.Id, costCenter.Id, nextCode++, type, competency, sourceCompetency, value,
                justification, counterparts);

        var awaitingTriage = Adjustment(BudgetAdjustmentType.Supplementation, 18_000m,
            "Reajuste do contrato de limpeza acima do previsto na peça orçamentária.",
            "Manutenção do nível de limpeza contratado até o fim do exercício.");

        var awaitingApproval = Adjustment(BudgetAdjustmentType.Reduction, 7_500m,
            "Contrato de vigilância encerrado antes do fim da competência.", null);
        awaitingApproval.SendToApproval(c.ActorId, c.Now);

        var approved = Adjustment(BudgetAdjustmentType.Supplementation, 25_000m,
            "Suplementação para a compra emergencial de equipamentos de rede.",
            "Substituição dos switches de borda do campus.");
        approved.SendToApproval(c.ActorId, c.Now);
        approved.Approve(c.ActorId, c.Now, approverLimit: null);

        var reversed = Adjustment(BudgetAdjustmentType.Transfer, 12_000m,
            "Remanejamento entre competências, revertido após revisão da controladoria.",
            "Antecipação da compra de materiais de laboratório.",
            sourceCompetency: Competency.AddMonths(competency, -1));
        reversed.SendToApproval(c.ActorId, c.Now);
        reversed.Approve(c.ActorId, c.Now, approverLimit: null);
        reversed.Reverse(c.ActorId, c.Now, "Remanejamento revertido: a despesa ficou na competência original.");

        var cancelled = Adjustment(BudgetAdjustmentType.Supplementation, 4_200m,
            "Solicitação duplicada — cancelada pela própria unidade.",
            "Duplicata da suplementação de limpeza.");
        cancelled.Cancel(c.ActorId, c.Now, "Duplicidade");

        var adjustments = new[] { awaitingTriage, awaitingApproval, approved, reversed, cancelled };
        await db.Set<BudgetAdjustment>().AddRangeAsync(adjustments, ct);

        // The approved supplementation must be visible in the ceiling too: in production the endpoint mutates the
        // cell inside the same SaveChanges. The reversed transfer nets to zero, so it is deliberately not applied.
        var cell = await db.Set<CostCenterUnitBudget>()
            .FirstOrDefaultAsync(b => b.CostCenterId == costCenter.Id && b.UnitId == unit.Id
                && b.Competency == competency, ct);
        if (cell is null)
            await db.Set<CostCenterUnitBudget>().AddAsync(
                new CostCenterUnitBudget(costCenter.Id, unit.Id, c.CompanyId, competency, approved.Value), ct);
        else
            cell.Increase(approved.Value);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Showcase seed: {Projects} projects (Active/Finished/Cancelled) + {Adjustments} budget adjustments "
            + "across every BudgetAdjustmentStatus.", projects.Length, adjustments.Length);
    }
}
