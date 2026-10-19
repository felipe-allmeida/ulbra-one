using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Accounting.Application.Seeding;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Domain;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Persistence;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Api;

/// <summary>
/// Populates the DEV database with sample data (companies, units, cost centers,
/// vendors, customers, employees and transactions). Idempotent: only runs if no
/// company exists. To reseed, recreate the database (docker compose down -v).
/// </summary>
public static class DevDataSeeder
{
    /// <summary>
    /// Creates local test users — one per role — in Development, to exercise claim-based
    /// authorization and role-based rendering via <c>/auth/dev-token</c> (which respects the
    /// role of already-existing users). Idempotent. Does not run in production
    /// (only called from the <c>IsDevelopment</c> block in Program.cs).
    /// Emails: <c>{admin,financeiro,controladoria,aprovador,contabil,solicitante}@ulbra.local</c>.
    /// </summary>
    public static async Task SeedLocalUsersAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var wanted = new (string Email, string Name, string Role)[]
        {
            ("admin@ulbra.local", "Admin (dev)", Roles.Admin),
            ("presidencia@ulbra.local", "Presidency (dev)", Roles.Presidency),
            ("financeiro@ulbra.local", "Finance (dev)", Roles.Finance),
            ("controladoria@ulbra.local", "Controllership (dev)", Roles.Controllership),
            ("aprovador@ulbra.local", "Aprovador (dev)", Roles.Approver),
            ("contabil@ulbra.local", "Accounting (dev)", Roles.Accounting),
            ("solicitante@ulbra.local", "Requester (dev)", Roles.Requester),
        };

        var emails = wanted.Select(u => u.Email).ToArray();
        var existing = await db.Set<User>()
            .Where(u => emails.Contains(u.Email))
            .ToListAsync(ct);
        var byEmail = existing.ToDictionary(u => u.Email);

        var added = 0;
        var healed = 0;
        foreach (var (email, name, role) in wanted)
        {
            if (byEmail.TryGetValue(email, out var user))
            {
                // Reconcile a stale role: users seeded before the PT→EN rename kept the
                // old name (e.g. "Financeiro"), which no longer matches any `role` value, so
                // the dev-token would issue a token with no permissions (→ "your access is being configured" screen).
                if (user.Role != role) { user.SetRole(role); healed++; }
                continue;
            }
            await db.Set<User>().AddAsync(new User(email, name, role), ct);
            added++;
        }

        if (added > 0 || healed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Seed: {Added} usuário(s) de cargo criado(s), {Healed} cargo(s) reconciliado(s) (admin/financeiro/controladoria/aprovador/contabil/solicitante @ulbra.local).",
                added, healed);
        }

        // The Controladoria role carries budget-adjustments:approve, but approving an adequação is a per-user
        // grant on top of the role (AEL-883). Enable it for the dev Controladoria user so the local flow works.
        var controladoria = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == "controladoria@ulbra.local", ct);
        if (controladoria is { CanApproveBudgetAdjustment: false })
        {
            controladoria.SetCanApproveBudgetAdjustment(true);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Dev-only: assigns the seeded requisitions to the dev requester (<c>solicitante@ulbra.local</c>)
    /// so they show under "My requisitions". The seed runs before the dev users exist and the AuditInterceptor
    /// stamps <c>CreatedBy="system"</c> outside a request, so we fix the owner once the user exists (a requisition
    /// in FinancialComplement/Complement/Draft then lands in the requester's "needs you"). ExecuteUpdate bypasses
    /// the interceptor (no re-stamp); idempotent (only touches <c>CreatedBy="system"</c>).</summary>
    public static async Task AssignSeedRequisitionsToRequesterAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var requester = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == "solicitante@ulbra.local", ct);
        if (requester is null) return;
        var ownerId = requester.Id.ToString();
        await db.Set<Requisition>()
            .Where(r => r.CreatedBy == "system")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedBy, ownerId), ct);
    }

    /// <summary>
    /// Grants the dev role users (non-Admin) access to every seeded company, to exercise
    /// per-resource authorization (AEL-764) without being fail-closed. Idempotent. Dev-only.
    /// </summary>
    public static async Task SeedLocalUserScopeGrantsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var companyIds = await db.Set<Company>().Select(c => c.Id).ToListAsync(ct);
        if (companyIds.Count == 0) return;

        // Only bootstrap users that have NO company scope yet (the seed/demo users). Users that already carry
        // grants — e.g. imported from the legacy with their real per-unit scope — are left untouched, so the
        // seed never blankets a scoped user with all-company access.
        var userIds = await db.Set<User>()
            .Where(u => u.Role != Roles.Admin && !db.Set<UserScopeGrant>().Any(g => g.UserId == u.Id))
            .Select(u => u.Id).ToListAsync(ct);
        var have = (await db.Set<UserScopeGrant>().Select(g => new { g.UserId, g.CompanyId }).ToListAsync(ct))
            .Select(g => (g.UserId, g.CompanyId)).ToHashSet();

        var added = 0;
        foreach (var userId in userIds)
            foreach (var companyId in companyIds)
                if (have.Add((userId, companyId)))
                {
                    await db.Set<UserScopeGrant>().AddAsync(new UserScopeGrant(userId, companyId), ct);
                    added++;
                }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: {Added} company-scope grant(s) granted to role users.", added);
        }
    }

    /// <summary>
    /// Seeds the unified scope (all units + all cost centers) for the dev scoped-role users (Requester and
    /// Approver), so they aren't fail-closed under AEL-883 (empty scope = create/approve/see nothing). Companies
    /// are already granted by <see cref="SeedLocalUserScopeGrantsAsync"/>. Snapshot of the current dimensions;
    /// idempotent; dev-only. Admins can narrow it later on the users screen.
    /// </summary>
    public static async Task SeedLocalScopedRoleGrantsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        // Only scoped-role users that have NO unit scope yet (the seed/demo users). Imported users already
        // carry their real per-unit/nature scope, so leave them intact instead of granting all units/CCs.
        var scopedUserIds = await db.Set<User>()
            .Where(u => (u.Role == Roles.Requester || u.Role == Roles.Approver)
                        && !db.Set<UserUnitScopeGrant>().Any(g => g.UserId == u.Id))
            .Select(u => u.Id).ToListAsync(ct);
        if (scopedUserIds.Count == 0) return;

        var units = await db.Set<Unit>().Select(u => new { u.Id, u.CompanyId }).ToListAsync(ct);
        var costCenters = await db.Set<CostCenter>().Select(c => new { c.Id, c.CompanyId }).ToListAsync(ct);
        if (units.Count == 0 && costCenters.Count == 0) return;

        var haveUnits = (await db.Set<UserUnitScopeGrant>().Select(x => new { x.UserId, x.UnitId }).ToListAsync(ct))
            .Select(x => (x.UserId, x.UnitId)).ToHashSet();
        var haveCostCenters = (await db.Set<UserCostCenterScopeGrant>().Select(x => new { x.UserId, x.CostCenterId }).ToListAsync(ct))
            .Select(x => (x.UserId, x.CostCenterId)).ToHashSet();

        var added = 0;
        foreach (var userId in scopedUserIds)
        {
            foreach (var u in units)
                if (haveUnits.Add((userId, u.Id)))
                {
                    await db.Set<UserUnitScopeGrant>().AddAsync(new UserUnitScopeGrant(userId, u.CompanyId, u.Id), ct);
                    added++;
                }
            foreach (var c in costCenters)
                if (haveCostCenters.Add((userId, c.Id)))
                {
                    await db.Set<UserCostCenterScopeGrant>().AddAsync(new UserCostCenterScopeGrant(userId, c.CompanyId, c.Id), ct);
                    added++;
                }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: {Added} unit/cost-center scope grant(s) for scoped-role users (Requester/Approver).", added);
        }
    }

    /// <summary>
    /// Security migration (all environments): with fail-closed blob access (AEL-764), grants the single
    /// company to non-Admin users that don't yet have any grant, so existing users aren't broken.
    /// Guarded by single-company — once multi-company exists the grants become explicit (it stops
    /// granting automatically). Idempotent; runs in every environment at startup.
    /// </summary>
    public static async Task BackfillUserScopeGrantsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var companyIds = await db.Set<Company>().Select(c => c.Id).ToListAsync(ct);
        if (companyIds.Count != 1) return;
        var companyId = companyIds[0];

        var userIds = await db.Set<User>()
            .Where(u => u.Role != Roles.Admin && !db.Set<UserScopeGrant>().Any(g => g.UserId == u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (userIds.Count == 0) return;

        foreach (var userId in userIds)
            await db.Set<UserScopeGrant>().AddAsync(new UserScopeGrant(userId, companyId), ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Scope backfill (AEL-764): {Count} user(s) with no grant were granted access to the single company.", userIds.Count);
    }

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var core = db;
        var finance = db;
        var registry = db;
        var time = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        await SeedExpenseCategoriesAsync(finance, logger, ct);
        await SeedGlobalChartAsync(finance, logger, ct);
        await SeedWithholdingRulesAsync(finance, logger, ct);

        var existing = await core.Set<Company>().CountAsync(ct);
        if (existing > 0)
        {
            logger.LogInformation("Seed: database already populated ({Count} company(ies)) — skipping. To reseed: docker compose down -v.", existing);
            return;
        }

        logger.LogInformation("Seed: database is empty — seeding sample data…");

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // Org structure (companies/units/cost centers + coverage) — shared with Staging, idempotent create-or-load.
        var org = await SeedOrgStructureAsync(core, ct);
        var celsp = org.Celsp;
        var ulbra = org.Ulbra;
        var canoas = org.Canoas;
        var gravatai = org.Gravatai;
        var ead = org.Ead;
        var ensino = org.Ensino;
        var marketing = org.Marketing;
        var tecnologia = org.Tecnologia;
        var suporte = org.Suporte;

        // ---------- Bank accounts (outflow accounts for payables) ----------
        if (!await finance.Set<BankAccount>().AnyAsync(ct))
        {
            // Ledger account each demo bank settles against (analytical/active/postable in the AELBRA chart,
            // seeded above). The chart has no "Santander" account, so the demo Santander accounts map to
            // distinct Banrisul analytical accounts — the point is a valid, non-fallback account so a
            // settlement credits it (not the global CashCode 1.2.05.15.015) in the journal.
            var santanderMovimentoLedger = await db.Set<LedgerAccount>().FirstAsync(a => a.Code == "1.2.05.15.030", ct);
            var bancoDoBrasilLedger = await db.Set<LedgerAccount>().FirstAsync(a => a.Code == "1.2.05.15.150", ct);
            var celspSantanderLedger = await db.Set<LedgerAccount>().FirstAsync(a => a.Code == "1.2.05.15.045", ct);

            finance.Set<BankAccount>().AddRange(
                new BankAccount(ulbra.Id, "033", "Banco Santander", "1234", "00012345", "6", BankAccountType.Checking, "Santander - Conta Movimento", 250_000m, ledgerAccountId: santanderMovimentoLedger.Id),
                new BankAccount(ulbra.Id, "001", "Banco do Brasil", "5678", "00098765", "4", BankAccountType.Checking, "Banco do Brasil - Conta Principal", 180_000m, ledgerAccountId: bancoDoBrasilLedger.Id),
                new BankAccount(celsp.Id, "033", "Banco Santander", "4321", "00054321", "0", BankAccountType.Checking, "Santander - CELSP", 60_000m, ledgerAccountId: celspSantanderLedger.Id));
            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Seed: 3 bank accounts seeded.");
        }

        // Make the demo Santander account able to generate CNAB payment remittances: it needs a
        // CNAB agreement code + a default payment method. Idempotent — also enriches a
        // pre-existing DB (the block above is skipped once any account exists), so an already-seeded
        // environment becomes remittance-ready on the next startup without a DB reset.
        var payerAccount = await finance.Set<BankAccount>()
            .FirstOrDefaultAsync(b => b.CompanyId == ulbra.Id && b.BankCode == "033", ct);
        if (payerAccount is not null)
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(payerAccount.CnabAgreementCode))
            {
                payerAccount.Update(payerAccount.BankCode, payerAccount.BankName, payerAccount.Agency,
                    payerAccount.AccountNumber, payerAccount.AccountDigit, payerAccount.AccountType,
                    payerAccount.Alias, payerAccount.OpeningBalance, "1234567");
                changed = true;
            }
            // A payment method must resolve for an installment to be eligible (payable override OR the
            // paying account's default). Mark this account the default payer with TED so the demo payables
            // (no per-payable method) resolve to TED and become eligible.
            var hasDefaultPayer = await finance.Set<BankAccount>()
                .AnyAsync(b => b.CompanyId == ulbra.Id && b.IsDefaultForPayments, ct);
            if (!hasDefaultPayer) { payerAccount.SetAsDefaultPayer(); changed = true; }
            if (changed)
            {
                await finance.SaveChangesAsync(ct);
                logger.LogInformation("Seed: Santander account set up for CNAB remittance (convênio + default payer TED).");
            }
        }

        // ---------- Vendors ----------
        Address Addr(string cep, string street, string number, string bairro, string city, string uf) =>
            new(cep, street, number, null, bairro, city, uf);

        var vGoogle = new VendorEntity(PersonType.Company, "Google Brasil Internet Ltda.", "Google",
            "06990590000123", "ads@google.com", "1140003000", Addr("04538133", "Av. Brigadeiro Faria Lima", "3477", "Itaim Bibi", "São Paulo", "SP"),
            "30 dias", new BankInfo("341", "0001", "123456", "6", ""), TaxRegime.LucroReal, PaymentSchedule.Default);
        var vCeee = new VendorEntity(PersonType.Company, "CEEE Equatorial Energia S.A.", "CEEE",
            "08467115000100", "atendimento@ceee.com.br", "5132000000", Addr("90230000", "Rua dos Andradas", "1000", "Centro", "Porto Alegre", "RS"),
            "à vista", BankInfo.Empty, TaxRegime.LucroReal, PaymentSchedule.Default);
        var vMicrosoft = new VendorEntity(PersonType.Company, "Microsoft Informática Ltda.", "Microsoft",
            "60316817000103", "licen@microsoft.com", "1150004000", Addr("04711130", "Av. das Nações Unidas", "12901", "Brooklin", "São Paulo", "SP"),
            "anual", BankInfo.Empty, TaxRegime.LucroReal, PaymentSchedule.Default);
        var vGrafica = new VendorEntity(PersonType.Company, "Gráfica Sul Impressões Ltda.", "Gráfica Sul",
            "92749225000180", "comercial@graficasul.com.br", "5133334444", Addr("92020000", "Rua Marechal Floriano", "250", "Centro", "Canoas", "RS"),
            "28 dias", BankInfo.Empty, TaxRegime.SimplesNacional, PaymentSchedule.Default);
        var vLimpeza = new VendorEntity(PersonType.Company, "Limpa Bem Serviços Ltda.", "Limpa Bem",
            "11444777000161", "contato@limpabem.com.br", "5133335555", Addr("94010000", "Av. Getúlio Vargas", "800", "Centro", "Gravataí", "RS"),
            "30 dias", BankInfo.Empty, TaxRegime.SimplesNacional, PaymentSchedule.Default);
        var vJoao = new VendorEntity(PersonType.Individual, "João Pedro Consultoria", null,
            "52998224725", "redacted-09@example.invalid", "51999990000", Addr("90560000", "Rua Mostardeiro", "100", "Moinhos de Vento", "Porto Alegre", "RS"),
            "à vista", BankInfo.Empty, TaxRegime.Mei, PaymentSchedule.Default);
        registry.Set<VendorEntity>().AddRange(vGoogle, vCeee, vMicrosoft, vGrafica, vLimpeza, vJoao);

        // ---------- Customers ----------
        var cPrefCanoas = new CustomerEntity(ulbra.Id, PersonType.Company, "Prefeitura Municipal de Canoas", "PMC",
            "88815415000100", "convenios@canoas.rs.gov.br", "5134634000", Addr("92010000", "Rua Frei Orlando", "199", "Centro", "Canoas", "RS"), 200_000m);
        var cHospital = new CustomerEntity(ulbra.Id, PersonType.Company, "Hospital Universitário de Canoas", "HU",
            "10520986000110", "financeiro@huc.com.br", "5134770000", Addr("92425900", "Av. Farroupilha", "8001", "São José", "Canoas", "RS"), 150_000m);
        var cMaria = new CustomerEntity(ulbra.Id, PersonType.Individual, "Maria Eduarda Souza", null,
            "39053344705", "redacted-14@example.invalid", "51988887777", Addr("92020100", "Rua Santos Dumont", "55", "Centro", "Canoas", "RS"), 20_000m);
        var cCarlos = new CustomerEntity(ulbra.Id, PersonType.Individual, "Carlos Henrique Lima", null,
            "23185644780", "redacted-04@example.invalid", "51977776666", Addr("94010100", "Rua Dom Pedro II", "300", "Centro", "Gravataí", "RS"), 20_000m);
        registry.Set<CustomerEntity>().AddRange(cPrefCanoas, cHospital, cMaria, cCarlos);

        // ---------- Employees ----------
        registry.Set<EmployeeEntity>().AddRange(
            new EmployeeEntity(ulbra.Id, "Ana Beatriz Ferreira", "11144477735", "redacted-01@example.invalid", "51999991111",
                "Analista de Marketing", canoas.Id, marketing.Id, today.AddYears(-2), Addr("92020200", "Rua Tiradentes", "120", "Centro", "Canoas", "RS")),
            new EmployeeEntity(ulbra.Id, "Bruno Carvalho", "30880150652", "redacted-02@example.invalid", "51999992222",
                "Coordenador de TI", canoas.Id, tecnologia.Id, today.AddYears(-4), Addr("90040000", "Av. Ipiranga", "6681", "Partenon", "Porto Alegre", "RS")),
            new EmployeeEntity(ulbra.Id, "Carla Menezes", "64426828530", "redacted-03@example.invalid", "51999993333",
                "Analista de RH", canoas.Id, suporte.Id, today.AddYears(-1), Addr("92010100", "Rua Quinze de Janeiro", "40", "Centro", "Canoas", "RS")),
            new EmployeeEntity(ulbra.Id, "Diego Rodrigues", "85245484300", "redacted-05@example.invalid", "51999994444",
                "Técnico de Infraestrutura", gravatai.Id, suporte.Id, today.AddYears(-3), Addr("94010200", "Rua Júlio de Castilhos", "75", "Centro", "Gravataí", "RS")),
            new EmployeeEntity(ulbra.Id, "Eduarda Pires", "29537995890", "redacted-06@example.invalid", "51999995555",
                "Bibliotecária", canoas.Id, ensino.Id, today.AddMonths(-8), Addr("92020300", "Rua General Câmara", "200", "Centro", "Canoas", "RS")));

        await registry.SaveChangesAsync(ct);

        // ---------- Payables (accounts-payable aggregate) ----------
        // Resolve a few categories by name to link payables to.
        var categories = await finance.Set<ExpenseCategory>().ToListAsync(ct);
        ExpenseCategory? Cat(string name) => categories.FirstOrDefault(c => c.Name == name);

        Payable PayableVista(CostCenter cc, string desc, VendorEntity vendor, ExpenseCategory? category,
            string? documentNumber, decimal amount, int dueInDays)
        {
            var installments = new List<Installment> { new(1, today.AddDays(dueInDays), amount) };
            var p = new Payable(ulbra.Id, PayableOrigin.Manual, vendor.Id, vendor.Name, documentNumber,
                today, desc, cc.Id, category?.Id, installments);
            p.ApplyAutoApproval(cc.AutoApprovalLimit, time.GetUtcNow());
            return p;
        }
        var pLimpeza = PayableVista(suporte, "Serviço de limpeza - mensal", vLimpeza, Cat("Limpeza e conservação"), "NF-LB-0098", 1_800m, 10); // classified then auto-approved → Scheduled (1 800 ≤ 2 000 limit; see classify block below)
        var pGoogle = PayableVista(marketing, "Google Ads - campanha vestibular", vGoogle, Cat("Marketing e captação de alunos"), "NF-2026-0451", 12_000m, -5);
        if (pGoogle.Status != PayableStatus.Scheduled) pGoogle.Approve("system", time.GetUtcNow());

        var pEnergiaGravatai = PayableVista(suporte, "Conta de energia - Campus Gravataí", vCeee, Cat("Energia elétrica"), "NF-CEEE-77121", 21_300m, 3);
        // Above INFRA's auto-approval limit (2_000), so approve explicitly: its open
        // installment is the counterpart of the seeded Suggested bank transaction and
        // a reconciliation can only confirm against an approved payable.
        if (pEnergiaGravatai.Status != PayableStatus.Scheduled) pEnergiaGravatai.Approve("system", time.GetUtcNow());
        // Only payables that reach advanced stages are kept (Paid / PendingAccounting / Scheduled) so the payable
        // pipeline still has examples. The former "Entrada" (Received) payables were dropped (AEL-653): the intake
        // of the expense flow is now a Requisition — seeded in SeedRequisitionsAsync (which also generates the two
        // requisition-sourced payables below in the InPayment/Paid stages).
        await finance.Set<Payable>().AddRangeAsync(new[]
        {
            pGoogle,          // → Paid (full payment below)
            pEnergiaGravatai, // PendingAccounting + classified (counterpart of the reconciliation debit)
            pLimpeza,         // → Scheduled, partially paid below
        }, ct);

        // ---------- Receivables (kept on Transaction) ----------
        Transaction Receive(Guid unitId, CostCenter cc, string desc, CustomerEntity customer, decimal amount, int dueInDays, TransactionStatus status)
        {
            var t = new Transaction(ulbra.Id, unitId, cc.Id, TransactionType.Receivable, desc, null, customer.Id, customer.Name, amount, today.AddDays(dueInDays));
            ApplyStatus(t, status, cc.AutoApprovalLimit);
            return t;
        }

        finance.Set<Transaction>().AddRange(
            Receive(canoas.Id, ensino, "Convênio de estágios 2026/1", cPrefCanoas, 80_000m, 5, TransactionStatus.Pending),
            Receive(canoas.Id, ensino, "Convênio residência médica", cHospital, 120_000m, -3, TransactionStatus.Approved),
            Receive(canoas.Id, ensino, "Mensalidade - Medicina (Maria Eduarda)", cMaria, 6_800m, 8, TransactionStatus.Pending),
            Receive(gravatai.Id, ensino, "Mensalidade - Direito (Carlos Henrique)", cCarlos, 2_400m, -1, TransactionStatus.Paid),
            Receive(ead.Id, ensino, "Mensalidade EAD - turma Pedagogia", cMaria, 980m, 18, TransactionStatus.Pending));

        await finance.SaveChangesAsync(ct);

        // ---------- Accounting (accounting core): global chart template + a few posted journal entries ----------
        // Seed the global ledger before classifying payables (classification targets the ledger chart).
        await SeedAccountingAsync(db, ulbra.Id, time, logger, ct);

        // ---------- Classify a few payables (so suggestion has history + the screen has examples) ----------
        // Look up seeded analytical ledger accounts by code; classify the energy payable (4.2.15.05.020 ENERGIA
        // ELETRICA) and the services payable (4.2.15.10.200 OUTROS SERVICOS PRESTADOS). Idempotent: only when unset.
        var accEnergia = await db.Set<LedgerAccount>().FirstAsync(a => a.Code == "4.2.15.05.020", ct);
        var accServicos = await db.Set<LedgerAccount>().FirstAsync(a => a.Code == "4.2.15.10.200", ct);

        if (pEnergiaGravatai.AccountingAccountId == null)
            pEnergiaGravatai.Classify(accEnergia.Id);
        // pLimpeza: classify first (IsClassified gate), then re-run auto-approval so it advances to Scheduled.
        // ApplyAutoApproval called at create-time was a silent no-op because IsClassified was false then.
        if (pLimpeza.AccountingAccountId == null)
        {
            pLimpeza.Classify(accServicos.Id);
            pLimpeza.ApplyAutoApproval(suporte.AutoApprovalLimit, time.GetUtcNow()); // → Scheduled (1 800 ≤ 2 000 limit)
        }
        // pGoogle: above auto-approval limit (12 000 > 2 000) so it won't self-schedule — classify + complete
        // accounting explicitly to reach Scheduled, letting the full payment below settle it to Paid
        // (RecomputeSettlement only re-derives from Scheduled/Paid — AEL-948). Idempotent.
        if (pGoogle.AccountingAccountId == null) pGoogle.Classify(accServicos.Id);
        if (pGoogle.Status == PayableStatus.PendingAccounting)
            pGoogle.CompleteAccounting("system", time.GetUtcNow(), hasFiscalDocument: true);
        await finance.SaveChangesAsync(ct);

        // ---------- Classification policy (per company, both flags OFF so existing flows are unaffected) ----------
        foreach (var company in new[] { celsp, ulbra })
        {
            if (!await finance.Set<ClassificationPolicy>().AnyAsync(p => p.CompanyId == company.Id, ct))
                await finance.Set<ClassificationPolicy>().AddAsync(new ClassificationPolicy(company.Id), ct);
        }
        await finance.SaveChangesAsync(ct);

        // ---------- Payments (settlement) — show settled & partially-paid states in the UI ----------
        if (!await finance.Set<Payment>().AnyAsync(ct))
        {
            var ulbraBank = await finance.Set<BankAccount>().FirstAsync(b => b.CompanyId == ulbra.Id, ct);
            var now = time.GetUtcNow();
            var paidOn = today.AddDays(-1);

            // Full payment → settles the Google Ads payable.
            var fullInstallment = pGoogle.Installments[0];
            var fullPayment = new Payment(fullInstallment.Id, pGoogle.Id, paidOn, fullInstallment.Amount,
                ulbraBank.Id, PaymentMethod.Pix, 0, 0, 0, null, Guid.Empty, now);
            fullInstallment.RegisterPayment(fullPayment);
            pGoogle.RecomputeSettlement();
            await finance.Set<Payment>().AddAsync(fullPayment, ct);

            // Partial payment → leaves the limpeza payable partially paid.
            var partialInstallment = pLimpeza.Installments[0];
            var partialPayment = new Payment(partialInstallment.Id, pLimpeza.Id, paidOn,
                Math.Round(partialInstallment.Amount / 2, 2), ulbraBank.Id, PaymentMethod.Boleto, 0, 0, 0, null, Guid.Empty, now);
            partialInstallment.RegisterPayment(partialPayment);
            pLimpeza.RecomputeSettlement();
            await finance.Set<Payment>().AddAsync(partialPayment, ct);

            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Seed: 2 payments seeded (1 full/settled, 1 partial).");
        }

        // ---------- Bank statement import + transactions (reconciliation) ----------
        if (!await finance.Set<BankTransaction>().AnyAsync(ct))
        {
            var ulbraBank = await finance.Set<BankAccount>().FirstAsync(b => b.CompanyId == ulbra.Id, ct);
            var now = time.GetUtcNow();
            var energyInstallment = pEnergiaGravatai.Installments[0];

            var import = new BankStatementImport(
                ulbra.Id, ulbraBank.Id, StatementSource.OfxUpload, "extrato-santander.ofx",
                today.AddDays(-7), today.AddDays(7), 2, 2, 0);

            // Debit that exactly matches the open energy installment within the date window → Suggested.
            var debit = new BankTransaction(import.Id, ulbra.Id, ulbraBank.Id, "SEED-TXN-DEBIT-1",
                today, -energyInstallment.Amount, "PAG FORNEC CEEE", now);
            debit.Suggest(energyInstallment.Id);

            // Credit with no counterpart → stays Unmatched.
            var credit = new BankTransaction(import.Id, ulbra.Id, ulbraBank.Id, "SEED-TXN-CREDIT-1",
                today, 2_500m, "DEPOSITO", now);

            await finance.Set<BankStatementImport>().AddAsync(import, ct);
            await finance.Set<BankTransaction>().AddAsync(debit, ct);
            await finance.Set<BankTransaction>().AddAsync(credit, ct);
            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Seed: 1 statement import + 2 bank transactions seeded (1 suggested debit, 1 unmatched credit).");
        }

        // ---------- Receivables (accounts-receivable aggregate, for cash-flow inflows) ----------
        // Spread due dates so the cash-flow projection has inflows across several future months,
        // plus one overdue installment to populate the "overdue receivables" line.
        if (!await finance.Set<Receivable>().AnyAsync(ct))
        {
            Receivable ReceivableParcelado(CostCenter cc, string desc, CustomerEntity customer,
                decimal total, int count, int firstDueInDays, int intervalMonths = 1)
            {
                var installments = Receivable.GenerateInstallments(total, count, today.AddDays(firstDueInDays), intervalMonths);
                return new Receivable(ulbra.Id, ReceivableOrigin.Manual, customer.Id, customer.Name, null,
                    today, desc, cc.Id, null, null, installments);
            }

            await finance.Set<Receivable>().AddRangeAsync(new[]
            {
                ReceivableParcelado(ensino, "Convênio de estágios 2026 (12x)", cPrefCanoas, 240_000m, 12, 12),
                ReceivableParcelado(ensino, "Convênio residência médica (6x)", cHospital, 90_000m, 6, 20),
                ReceivableParcelado(ensino, "Plano de mensalidades - Medicina", cMaria, 68_000m, 10, 8),
                ReceivableParcelado(ensino, "Mensalidades em atraso - Direito", cCarlos, 4_800m, 2, -20), // 1 overdue + 1 upcoming
            }, ct);
            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Seed: 4 receivables seeded (spread due dates + 1 overdue) for cash-flow inflows.");
        }

        // ---------- Reconciled-balance anchor (cash-flow starting balance) ----------
        // Inform a real balance on the main account as of a week ago; current balance = anchor +
        // movements after that date. Idempotent: only set when no account has an anchor yet.
        if (!await finance.Set<BankAccount>().AnyAsync(b => b.ReconciledBalance != null, ct))
        {
            var mainBank = await finance.Set<BankAccount>().FirstAsync(b => b.CompanyId == ulbra.Id, ct);
            mainBank.SetReconciledBalance(300_000m, today.AddDays(-7));
            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Seed: reconciled-balance anchor set on '{Alias}'.", mainBank.Alias);
        }

        // ---------- Budgeting (monthly budget by Unit × CostCenter × Competency) ----------
        await SeedBudgetingAsync(db, ulbra.Id, time, logger, ct);

        // ---------- Requisitions (workflow Requester → Triage → Financial Complement → Approver → payment) ----------
        await SeedRequisitionsAsync(db, ulbra, tecnologia, suporte, marketing, canoas, ead, vMicrosoft, vGrafica, vJoao, time, logger, ct);

        logger.LogInformation(
            "Dev seed complete: {Companies} companies, {Units} units, {CostCenters} cost centers, " +
            "{Vendors} vendors, {Customers} customers, {Employees} employees, {Payables} payables, {Receivables} receivables.",
            14, 27, 20, 6, 4, 5, 3, 5);
    }

    /// <summary>References the seed flows need from the org structure (the rest is created but not returned).</summary>
    public sealed record OrgRefs(
        Company Celsp, Company Ulbra,
        Unit Canoas, Unit Gravatai, Unit Ead,
        CostCenter Ensino, CostCenter Marketing, CostCenter Tecnologia, CostCenter Suporte);

    /// <summary>Idempotent create-or-load of the AELBRA org (companies/units/cost centers + coverage). Builds it
    /// when the DB has no company; otherwise reloads and returns the references. One source of the org data,
    /// shared by the Dev seed and <see cref="StageDataSeeder"/>.</summary>
    public static async Task<OrgRefs> SeedOrgStructureAsync(AppDbContext core, CancellationToken ct = default)
    {
        if (await core.Set<Company>().AnyAsync(ct))
        {
            var celspExisting = await core.Set<Company>().FirstAsync(c => c.Cnpj == "88332466000103", ct);
            var ulbraExisting = await core.Set<Company>().FirstAsync(c => c.Cnpj == "88332466000294", ct);
            var unitsExisting = await core.Set<Unit>().Where(u => u.CompanyId == ulbraExisting.Id).ToListAsync(ct);
            var ccsExisting = await core.Set<CostCenter>().Where(c => c.CompanyId == ulbraExisting.Id).ToListAsync(ct);
            await EnsureImportOrgCompleteAsync(core, ulbraExisting, unitsExisting.First(u => u.Code == "1"), ct);
            return new OrgRefs(celspExisting, ulbraExisting,
                unitsExisting.First(u => u.Acronym == "CAN"), unitsExisting.First(u => u.Acronym == "GRA"), unitsExisting.First(u => u.Acronym == "EAD"),
                ccsExisting.First(c => c.Code == "ENS"), ccsExisting.First(c => c.Code == "MKT"), ccsExisting.First(c => c.Code == "TEC"), ccsExisting.First(c => c.Code == "SUP"));
        }

        // ---------- Companies ----------
        // CELSP + Ulbra S.A (the group's operating company). "Ulbra S.A" hosts the matriz + campuses as units.
        var celsp = new Company("Comunidade Evangélica Luterana São Paulo - CELSP", "88332466000103");
        var ulbra = new Company("Ulbra S.A", "88332466000294");
        // Separate legacy "empresas": each is one AELBRA establishment the group runs as a standalone company,
        // so it gets a single matriz unit + its own cost center. Real establishment CNPJs (raiz 88332580) from
        // the official "Relação de Mantidas da AELBRA".
        var hospitalVet = new Company("Hospital Veterinário - Canoas/RS", "88332580003261");
        var colCristoRedentor = new Company("Colégio Ulbra Cristo Redentor (Canoas)", "88332580000599");
        var colSaoLucas = new Company("Colégio Ulbra São Lucas (Sapucaia do Sul)", "88332580001056");
        var colMartinhoLutero = new Company("Colégio Ulbra Martinho Lutero (Guaíba)", "88332580004152");
        var colSaoPedro = new Company("Colégio Ulbra São Pedro (Cachoeira do Sul)", "88332580004667");
        var colAplicacao = new Company("Colégio Ulbra de Aplicação (Itumbiara)", "88332580005205");
        var colCristoSalvador = new Company("Colégio Ulbra Cristo Salvador (Santarém)", "88332580002370");
        // Non-AELBRA ventures (each its own PJ, own CNPJ root — except Ultec, an AELBRA establishment the group
        // still runs as a standalone company). Same standalone-company pattern (matriz unit + own cost center).
        var ultec = new Company("Ultec School", "88332580010128");
        var colPalmares = new Company("Colégio Palmares (São Paulo)", "05824890000170");
        var mw = new Company("MW", "61783501000186");
        var radio = new Company("Rádio", "91802702000144");
        var reativa = new Company("Reativa", "61270748000107");
        core.Set<Company>().AddRange(celsp, ulbra, hospitalVet, colCristoRedentor, colSaoLucas,
            colMartinhoLutero, colSaoPedro, colAplicacao, colCristoSalvador,
            ultec, colPalmares, mw, radio, reativa);

        // ---------- Units (Ulbra S.A: matriz + campuses/poles) ----------
        // Names + legacy Code from the legacy "empresa" catalog; establishment CNPJs (raiz 88332580) from the
        // official "Relação de Mantidas da AELBRA". The matriz (filial 1) carries the corporate naturezas.
        var matriz = new Unit(ulbra.Id, "Matriz", "MATRIZ", "88332580000165", isMatriz: true, code: "1");
        var canoas = new Unit(ulbra.Id, "Canoas/RS", "CAN", "88332580000670", code: "100");
        var torres = new Unit(ulbra.Id, "Torres/RS", "TOR", "88332580003857", code: "102");
        var guaiba = new Unit(ulbra.Id, "Guaíba/RS", "GUA", "88332580004071", code: "103");
        var saoJeronimo = new Unit(ulbra.Id, "São Jerônimo/RS", "SJE", "88332580004314", code: "104");
        var cachoeira = new Unit(ulbra.Id, "Cachoeira do Sul/RS", "CSU", "88332580004748", code: "106");
        var carazinho = new Unit(ulbra.Id, "Carazinho/RS", "CRZ", "88332580006791", code: "108");
        var portoAlegre = new Unit(ulbra.Id, "Porto Alegre/RS", "POA", "88332580008069", code: "109");
        // Legacy codes 110 (Santa Maria) and 202 (Gravataí) don't match the AELBRA filial order — CNPJ is the
        // main campus establishment (0062-87 / 0044-03), not the coincidental filial 110/202.
        var santaMaria = new Unit(ulbra.Id, "Santa Maria/RS", "SMA", "88332580006287", code: "110");
        var ead = new Unit(ulbra.Id, "EAD", "EAD", "88332580009111", code: "200");
        var gravatai = new Unit(ulbra.Id, "Gravataí/RS", "GRA", "88332580004403", code: "202");
        var santarem = new Unit(ulbra.Id, "Santarém/PA", "STM", "88332580001803", code: "301");
        var manaus = new Unit(ulbra.Id, "Manaus/AM", "MAO", "88332580002451", code: "302");
        var palmas = new Unit(ulbra.Id, "Palmas/TO", "PMS", "88332580002613", code: "303");
        var itumbiara = new Unit(ulbra.Id, "Itumbiara/GO", "ITU", "88332580002885", code: "400");
        core.Set<Unit>().AddRange(matriz, canoas, torres, guaiba, saoJeronimo, cachoeira, carazinho, portoAlegre,
            santaMaria, ead, gravatai, santarem, manaus, palmas, itumbiara);

        // Each separate company gets one matriz unit carrying its own establishment CNPJ + legacy code.
        var uHospitalVet = new Unit(hospitalVet.Id, "Hospital Veterinário - Canoas/RS", "MATRIZ", "88332580003261", isMatriz: true, code: "101");
        var uCristoRedentor = new Unit(colCristoRedentor.Id, "Colégio Ulbra Cristo Redentor (Canoas)", "MATRIZ", "88332580000599", isMatriz: true, code: "502");
        var uSaoLucas = new Unit(colSaoLucas.Id, "Colégio Ulbra São Lucas (Sapucaia do Sul)", "MATRIZ", "88332580001056", isMatriz: true, code: "504");
        var uMartinhoLutero = new Unit(colMartinhoLutero.Id, "Colégio Ulbra Martinho Lutero (Guaíba)", "MATRIZ", "88332580004152", isMatriz: true, code: "507");
        var uSaoPedro = new Unit(colSaoPedro.Id, "Colégio Ulbra São Pedro (Cachoeira do Sul)", "MATRIZ", "88332580004667", isMatriz: true, code: "509");
        var uAplicacao = new Unit(colAplicacao.Id, "Colégio Ulbra de Aplicação (Itumbiara)", "MATRIZ", "88332580005205", isMatriz: true, code: "551");
        var uCristoSalvador = new Unit(colCristoSalvador.Id, "Colégio Ulbra Cristo Salvador (Santarém)", "MATRIZ", "88332580002370", isMatriz: true, code: "571");
        var uUltec = new Unit(ultec.Id, "Ultec School", "MATRIZ", "88332580010128", isMatriz: true, code: "111");
        var uColPalmares = new Unit(colPalmares.Id, "Colégio Palmares (São Paulo)", "MATRIZ", "05824890000170", isMatriz: true, code: "600");
        var uMw = new Unit(mw.Id, "MW", "MATRIZ", "61783501000186", isMatriz: true, code: "601");
        var uRadio = new Unit(radio.Id, "Rádio", "MATRIZ", "91802702000144", isMatriz: true, code: "602");
        var uReativa = new Unit(reativa.Id, "Reativa", "MATRIZ", "61270748000107", isMatriz: true, code: "603");
        core.Set<Unit>().AddRange(uHospitalVet, uCristoRedentor, uSaoLucas, uMartinhoLutero, uSaoPedro, uAplicacao, uCristoSalvador,
            uUltec, uColPalmares, uMw, uRadio, uReativa);

        // ---------- Cost centers (naturezas) ----------
        // Ulbra S.A: 8 corporate naturezas owned by the matriz; "Ensino" is the shared operating natureza across
        // every campus. AutoApprovalLimit kept where a demo payable relies on it (Suporte 2 000 → pLimpeza).
        var comunicacao = new CostCenter(ulbra.Id, "COM", "Comunicação");
        var ensino = new CostCenter(ulbra.Id, "ENS", "Ensino");
        var jornada = new CostCenter(ulbra.Id, "JOR", "Jornada do Aluno");
        var juridico = new CostCenter(ulbra.Id, "JUR", "Jurídico");
        var marketing = new CostCenter(ulbra.Id, "MKT", "Marketing", 5_000m);
        var presidencia = new CostCenter(ulbra.Id, "PRE", "Presidência");
        var suporte = new CostCenter(ulbra.Id, "SUP", "Suporte", 2_000m);
        var tecnologia = new CostCenter(ulbra.Id, "TEC", "Tecnologia e Inovação", 8_000m);
        core.Set<CostCenter>().AddRange(comunicacao, ensino, jornada, juridico, marketing, presidencia, suporte, tecnologia);

        // Separate companies: one "Ensino" cost center each (Code unique per company).
        var ccHospitalVet = new CostCenter(hospitalVet.Id, "ENS", "Ensino");
        var ccCristoRedentor = new CostCenter(colCristoRedentor.Id, "ENS", "Ensino");
        var ccSaoLucas = new CostCenter(colSaoLucas.Id, "ENS", "Ensino");
        var ccMartinhoLutero = new CostCenter(colMartinhoLutero.Id, "ENS", "Ensino");
        var ccSaoPedro = new CostCenter(colSaoPedro.Id, "ENS", "Ensino");
        var ccAplicacao = new CostCenter(colAplicacao.Id, "ENS", "Ensino");
        var ccCristoSalvador = new CostCenter(colCristoSalvador.Id, "ENS", "Ensino");
        var ccUltec = new CostCenter(ultec.Id, "ENS", "Ensino");
        var ccColPalmares = new CostCenter(colPalmares.Id, "PAL", "Palmares");
        var ccMw = new CostCenter(mw.Id, "MW", "MW");
        var ccRadio = new CostCenter(radio.Id, "RAD", "Rádio");
        var ccReativa = new CostCenter(reativa.Id, "REA", "Reativa");
        core.Set<CostCenter>().AddRange(ccHospitalVet, ccCristoRedentor, ccSaoLucas, ccMartinhoLutero, ccSaoPedro, ccAplicacao, ccCristoSalvador,
            ccUltec, ccColPalmares, ccMw, ccRadio, ccReativa);

        // Coverage (which units each cost center serves)
        core.Set<CostCenterUnit>().AddRange(
            // Ensino operates at the matriz and every campus.
            new CostCenterUnit(ensino.Id, matriz.Id),
            new CostCenterUnit(ensino.Id, canoas.Id), new CostCenterUnit(ensino.Id, torres.Id),
            new CostCenterUnit(ensino.Id, guaiba.Id), new CostCenterUnit(ensino.Id, saoJeronimo.Id),
            new CostCenterUnit(ensino.Id, cachoeira.Id), new CostCenterUnit(ensino.Id, carazinho.Id),
            new CostCenterUnit(ensino.Id, portoAlegre.Id), new CostCenterUnit(ensino.Id, santaMaria.Id),
            new CostCenterUnit(ensino.Id, ead.Id), new CostCenterUnit(ensino.Id, gravatai.Id),
            new CostCenterUnit(ensino.Id, santarem.Id), new CostCenterUnit(ensino.Id, manaus.Id),
            new CostCenterUnit(ensino.Id, palmas.Id), new CostCenterUnit(ensino.Id, itumbiara.Id),
            // The other corporate naturezas live at the matriz.
            new CostCenterUnit(comunicacao.Id, matriz.Id), new CostCenterUnit(jornada.Id, matriz.Id),
            new CostCenterUnit(juridico.Id, matriz.Id), new CostCenterUnit(presidencia.Id, matriz.Id),
            new CostCenterUnit(marketing.Id, matriz.Id), new CostCenterUnit(tecnologia.Id, matriz.Id),
            new CostCenterUnit(suporte.Id, matriz.Id),
            // Demo extension: Marketing/TEC/Suporte also cover Canoas + EAD so the seeded requisitions/payables
            // (remapped onto these naturezas) resolve a valid (cost center, unit) pair and budget cell.
            new CostCenterUnit(marketing.Id, canoas.Id),
            new CostCenterUnit(tecnologia.Id, canoas.Id), new CostCenterUnit(tecnologia.Id, ead.Id),
            new CostCenterUnit(suporte.Id, canoas.Id), new CostCenterUnit(suporte.Id, gravatai.Id),
            // Separate companies: each cost center serves its own matriz unit.
            new CostCenterUnit(ccHospitalVet.Id, uHospitalVet.Id),
            new CostCenterUnit(ccCristoRedentor.Id, uCristoRedentor.Id),
            new CostCenterUnit(ccSaoLucas.Id, uSaoLucas.Id),
            new CostCenterUnit(ccMartinhoLutero.Id, uMartinhoLutero.Id),
            new CostCenterUnit(ccSaoPedro.Id, uSaoPedro.Id),
            new CostCenterUnit(ccAplicacao.Id, uAplicacao.Id),
            new CostCenterUnit(ccCristoSalvador.Id, uCristoSalvador.Id),
            new CostCenterUnit(ccUltec.Id, uUltec.Id),
            new CostCenterUnit(ccColPalmares.Id, uColPalmares.Id),
            new CostCenterUnit(ccMw.Id, uMw.Id),
            new CostCenterUnit(ccRadio.Id, uRadio.Id),
            new CostCenterUnit(ccReativa.Id, uReativa.Id));

        await core.SaveChangesAsync(ct);

        await EnsureImportOrgCompleteAsync(core, ulbra, matriz, ct);

        return new OrgRefs(celsp, ulbra, canoas, gravatai, ead, ensino, marketing, tecnologia, suporte);
    }

    // Corporate naturezas referenced by the legacy orçamento import but absent from the base seed
    // (code synthesized, name in the One's title-case style). Kept minimal + idempotent.
    private static readonly (string Code, string Name)[] ImportNaturezas =
    [
        ("ECB", "Educação Corporativa e Business School"),
        ("RGC", "Relações Governamentais e Cidades"),
        ("STR", "Stricto Sensu"),
        ("TRA", "Transações"),
    ];

    /// <summary>Completes the org with the naturezas/unit that the legacy orçamento import references but
    /// the base seed doesn't create. Idempotent (adds only what's missing, keyed by code), so it also
    /// backfills an already-seeded environment on startup. The new corporate naturezas cover the matriz,
    /// like the other corporate ones; unit "604" (Esporte) is only referenced by user scope grants —
    /// its placement under Ulbra S.A is an assumption pending confirmation.</summary>
    private static async Task EnsureImportOrgCompleteAsync(AppDbContext core, Company ulbra, Unit matriz, CancellationToken ct)
    {
        var existingCodes = (await core.Set<CostCenter>()
            .Where(c => c.CompanyId == ulbra.Id).Select(c => c.Code).ToListAsync(ct)).ToHashSet();

        var newCostCenters = ImportNaturezas
            .Where(n => !existingCodes.Contains(n.Code))
            .Select(n => new CostCenter(ulbra.Id, n.Code, n.Name))
            .ToList();
        core.Set<CostCenter>().AddRange(newCostCenters);

        if (!await core.Set<Unit>().AnyAsync(u => u.Code == "604", ct))
            core.Set<Unit>().Add(new Unit(ulbra.Id, "Esporte", "ESP", null, code: "604"));

        if (newCostCenters.Count == 0 && !core.ChangeTracker.HasChanges())
            return;

        await core.SaveChangesAsync(ct);

        var coverage = newCostCenters.Select(cc => new CostCenterUnit(cc.Id, matriz.Id)).ToList();
        if (coverage.Count > 0)
        {
            core.Set<CostCenterUnit>().AddRange(coverage);
            await core.SaveChangesAsync(ct);
        }
    }

    /// <summary>Seeds the global AELBRA chart of accounts (once, shared by all companies) plus a handful of
    /// posted journal entries for the given company so the trial balance, DRE and balance sheet come up
    /// populated. Chart seed is idempotent globally; the journal entries are idempotent per company.</summary>
    private static async Task SeedAccountingAsync(AppDbContext db, Guid companyId, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        // Global chart of accounts (shared by all companies): seed once when empty.
        var accounts = await SeedGlobalChartAsync(db, logger, ct);

        // Per-company posted entries: skip when this company already has journal entries.
        if (await db.Set<JournalEntry>().AnyAsync(e => e.CompanyId == companyId, ct))
        {
            logger.LogInformation("Seed: journal entries already present for company — skipping accounting entries.");
            return;
        }

        var byCode = accounts.ToDictionary(a => a.Code);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var competence = new DateOnly(today.Year, today.Month, 1);
        var nextNumber = 1;

        // Helper: build a balanced two-line entry (debit + credit) and post it immediately.
        JournalEntry Post(string description, string debitCode, string creditCode, decimal amount, DateOnly when)
        {
            var entry = new JournalEntry(companyId, when, description);
            entry.AddLine(new JournalLine(byCode[debitCode].Id, EntrySide.Debit, amount));
            entry.AddLine(new JournalLine(byCode[creditCode].Id, EntrySide.Credit, amount));
            entry.Post(nextNumber++, now);
            return entry;
        }

        var entries = new List<JournalEntry>
        {
            // Paid-in share capital: cash inflow against equity.
            Post("Integralização do capital social", "1.2.05.05.020", "2.6.05.05.020", 500_000m, competence),
            // Operating revenue received into a bank account.
            Post("Receita operacional", "1.2.05.15.015", "3.2.05.10.020", 180_000m, competence),
            // Board payroll settled by the bank.
            Post("Remuneração da diretoria", "4.2.05.05.020", "1.2.05.15.015", 95_000m, competence.AddDays(4)),
            // Electricity settled by the bank.
            Post("Energia elétrica - competência", "4.2.15.05.020", "1.2.05.15.015", 21_300m, competence.AddDays(9)),
            // Third-party services (PJ) settled by the bank.
            Post("Serviços de terceiros (PJ)", "4.2.15.10.200", "1.2.05.15.015", 18_000m, competence.AddDays(9)),
        };

        db.Set<JournalEntry>().AddRange(entries);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: accounting seeded ({Accounts} ledger accounts + {Entries} posted journal entries).",
            accounts.Count, entries.Count);
    }

    /// <summary>Seeds example monthly budget cells across the real naturezas so the budget screens come up
    /// populated: Ensino across the matriz + a few campuses; Tecnologia e Inovação on Canoas/EAD to line up with
    /// the seeded requisitions (one tight cell drives the breach demo); Marketing/Suporte on Canoas for the
    /// non-TI requisitions. Real budget values come later from the CSV import. Idempotent by natural key
    /// (CostCenterUnitBudget by (CostCenterId, UnitId, Competency)).</summary>
    private static async Task SeedBudgetingAsync(AppDbContext db, Guid companyId, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // A 12-month horizon from the current competency — mirrors the product default (a budget for a whole year).
        var startCompetency = Competency.FromDate(now);
        var competencies = Competency.EnumerateRange(startCompetency, Competency.AddMonths(startCompetency, 11)).ToArray();
        var tightCompetency = Competency.AddMonths(startCompetency, 2);

        var costCenters = await db.Set<CostCenter>().Where(c => c.CompanyId == companyId).ToListAsync(ct);
        CostCenter? Cc(string code) => costCenters.FirstOrDefault(c => c.Code == code);
        var ensino = Cc("ENS"); var tecnologia = Cc("TEC"); var marketing = Cc("MKT"); var suporte = Cc("SUP");

        var units = await db.Set<Unit>().Where(u => u.CompanyId == companyId).ToListAsync(ct);
        Unit? U(string acronym) => units.FirstOrDefault(u => u.Acronym == acronym);
        var canoas = U("CAN"); var ead = U("EAD");
        if (ensino is null || tecnologia is null) return;

        var added = 0;
        async Task AddCell(CostCenter? cc, Unit? unit, string competency, decimal limit)
        {
            if (cc is null || unit is null) return;
            if (await db.Set<CostCenterUnitBudget>().AnyAsync(o => o.CostCenterId == cc.Id && o.UnitId == unit.Id && o.Competency == competency, ct))
                return;
            db.Set<CostCenterUnitBudget>().Add(new CostCenterUnitBudget(cc.Id, unit.Id, companyId, competency, limit));
            added++;
        }

        foreach (var competency in competencies)
        {
            // Ensino: matriz + a few campuses so the budget screens are populated.
            foreach (var acronym in new[] { "MATRIZ", "CAN", "GRA", "TOR", "EAD" })
                await AddCell(ensino, U(acronym), competency, 50_000m);

            // Tecnologia e Inovação — Canoas + EAD, matching the seeded requisitions. One deliberately tight cell
            // (Canoas, current+2) makes the "Locação de servidores" requisition (in Approval) breach the ceiling
            // → exercises the hard block + Adequação Orçamentária flow.
            await AddCell(tecnologia, canoas, competency, competency == tightCompetency ? 12_000m : 40_000m);
            await AddCell(tecnologia, ead, competency, 25_000m);

            // Marketing/Suporte on Canoas so the non-Tecnologia demo requisitions project against a ceiling.
            await AddCell(marketing, canoas, competency, 15_000m);
            await AddCell(suporte, canoas, competency, 10_000m);
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: budgeting seeded ({Budgets} example budget cell(s)).", added);
    }

    /// <summary>Seeds the requisition workflow (AEL-653): eight requisitions across the workflow stages (Draft,
    /// Triage, Financial Complement, Approval, InPayment, Paid), mostly on Tecnologia e Inovação to line up with
    /// the seeded budget. The two that reach payment also materialize their requisition-sourced Payable (Origin=Purchase,
    /// auto-scheduled without a ledger account → "classification review"), mirroring the Finance consumer.
    /// State is driven through the real domain transitions; the actor is the system (Guid.Empty) because dev
    /// users are seeded after this. Idempotent (skips when the company already has requisitions).</summary>
    private static async Task SeedRequisitionsAsync(
        AppDbContext db, Company ulbra, CostCenter tecnologia, CostCenter suporte, CostCenter marketing, Unit canoas, Unit ead,
        VendorEntity vMicrosoft, VendorEntity vGrafica, VendorEntity vJoao,
        TimeProvider time, ILogger logger, CancellationToken ct)
    {
        if (await db.Set<Requisition>().AnyAsync(r => r.CompanyId == ulbra.Id, ct))
        {
            logger.LogInformation("Seed: requisitions already present — skipping.");
            return;
        }

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var c0 = Competency.FromDate(now);      // current competency
        var c2 = Competency.AddMonths(c0, 2);   // tight Tecnologia/Canoas cell → breach demo (matches SeedBudgetingAsync)
        var c3 = Competency.AddMonths(c0, 3);   // end of the window for the monthly triage/approval requisitions
        var c11 = Competency.AddMonths(c0, 11); // 12-month window end
        var system = Guid.Empty;                // dev users don't exist yet (seeded after this)
        var code = 1;

        // A Draft requisition; the first due date is a placeholder (the financial complement re-authors the plan,
        // and the budget competency comes from Start/EndCompetency — not the due dates).
        Requisition New(Unit unit, CostCenter cc, RequisitionType type, string desc, decimal value,
            EstimateType estimateType, string startComp, VendorEntity vendor,
            string? endComp = null, PaymentFrequency? freq = null, int? count = null) =>
            new(ulbra.Id, unit.Id, cc.Id, code++, type, desc, value, estimateType, startComp, vendor.Id,
                today.AddDays(15), endComp, observation: null, freq, count);

        // Authored plan for the financial complement: `count` installments summing exactly to `total` (residual on
        // the last), monthly from 15 days out. Sum == total keeps it at/under the triage-approved value → Approval.
        List<(DateOnly DueDate, decimal Amount, PaymentMethod Method, string? BoletoDigitableLine, string? PixKeyOverride, string? PixPayloadEmv)> Plan(decimal total, int count, PaymentMethod method)
        {
            var each = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
            var firstDue = today.AddDays(15);
            var rows = new List<(DateOnly, decimal, PaymentMethod, string?, string?, string?)>(count);
            var acc = 0m;
            for (var i = 0; i < count; i++)
            {
                var amount = i == count - 1 ? total - acc : each;
                acc += amount;
                rows.Add((firstDue.AddMonths(i), amount, method, null, null, null));
            }
            return rows;
        }

        // Materializes the requisition-sourced Payable the Finance consumer would create: same installments,
        // stamped origin, auto-scheduled WITHOUT a ledger account (rides the "classification review" queue).
        Payable SchedulePayableFor(Requisition r, VendorEntity vendor, PayableType type)
        {
            var actor = $"system:req:{r.Id}";
            var installments = r.Installments.OrderBy(i => i.Sequence)
                .Select(i => new Installment(i.Sequence, i.DueDate, i.Amount, boletoDigitableLine: null, paymentMethod: i.PaymentMethod))
                .ToList();
            var p = new Payable(ulbra.Id, PayableOrigin.Purchase, vendor.Id, vendor.Name, documentNumber: null,
                r.IssueDate, r.Description, r.CostCenterId, categoryId: null, installments);
            p.SetOrigin(r.Id, r.UnitId);
            p.SetType(type);
            p.Approve(actor, now);                                                                   // → PendingAccounting
            p.CompleteAccounting(actor, now, hasFiscalDocument: false, allowWithoutFiscalDocument: true); // → Scheduled
            return p;
        }

        // #1 Draft — requester's draft (Tecnologia/Canoas).
        var r1 = New(canoas, tecnologia, RequisitionType.Purchase, "Notebooks para o laboratório de informática", 18_000m, EstimateType.Full, c0, vMicrosoft);

        // #2 Triage — awaiting Controllership (Tecnologia/EAD). MONTHLY service (Monthly, c0→c0+3) so the triage decision
        // screen shows the cost center's provision chart (line/area) — fits under the ceiling (EAD 25k/month).
        var r2 = New(ead, tecnologia, RequisitionType.ServiceProvision, "Suporte e manutenção de TI (contrato mensal)", 40_000m, EstimateType.Monthly, c0, vMicrosoft, endComp: c3, freq: PaymentFrequency.Monthly, count: 4);
        r2.Submit(system, now);

        // #3 Financial Complement — back with the requester (Tecnologia/EAD).
        var r3 = New(ead, tecnologia, RequisitionType.Purchase, "Upgrade de switches de rede do campus EAD", 22_000m, EstimateType.Full, c0, vMicrosoft);
        r3.Submit(system, now);
        r3.ApproveTriage(system, now);

        // #4 Approval — awaiting Approver. MONTHLY service (Monthly, c0→c0+2) on Tecnologia/Canoas: spreads the
        // cost across 3 competencies so the approval decision screen shows the provision chart (line/area),
        // and the tight month (Canoas, current+2 = 12k) makes the projection BREACH the ceiling → red marker + the
        // hard block of the Adequação Orçamentária.
        var r4 = New(canoas, tecnologia, RequisitionType.ServiceProvision, "Locação de servidores em nuvem (contrato mensal)", 60_000m, EstimateType.Monthly, c0, vMicrosoft, endComp: c2, freq: PaymentFrequency.Monthly, count: 3);
        r4.Submit(system, now);
        r4.ApproveTriage(system, now);
        r4.SubmitFinancialComplement(system, now, today, Plan(60_000m, 3, PaymentMethod.Ted));

        // #5 InPayment — approved; Payable auto-scheduled below. Monthly over the 12-month window.
        var r5 = New(canoas, tecnologia, RequisitionType.ServiceProvision, "Assinatura Microsoft 365 (anual)", 96_000m, EstimateType.Monthly, c0, vMicrosoft, endComp: c11, freq: PaymentFrequency.Monthly, count: 12);
        r5.Submit(system, now);
        r5.ApproveTriage(system, now);
        r5.SubmitFinancialComplement(system, now, today, Plan(96_000m, 12, PaymentMethod.Boleto));
        r5.Approve(system, now, candidateExceeds: false);
        var p5 = SchedulePayableFor(r5, vMicrosoft, PayableType.Service);

        // #6 Paid — full flow: approved → Payable scheduled → paid → realized amount fed back → requisition Paid.
        var r6 = New(canoas, tecnologia, RequisitionType.ServiceProvision, "Colocation de servidores (data center)", 26_700m, EstimateType.Full, c0, vMicrosoft);
        r6.Submit(system, now);
        r6.ApproveTriage(system, now);
        r6.SubmitFinancialComplement(system, now, today, Plan(26_700m, 1, PaymentMethod.Ted));
        r6.Approve(system, now, candidateExceeds: false);
        var p6 = SchedulePayableFor(r6, vMicrosoft, PayableType.Service);

        // Settlement: settle the installment → Payable Paid, then feed it back as the requisition's realized amount
        // (RegisterPayment advances the requisition to Paid once the realized amount covers the total).
        var payerBank = await db.Set<BankAccount>().FirstAsync(b => b.CompanyId == ulbra.Id, ct);
        var inst6 = p6.Installments[0];
        var pay6 = new Payment(inst6.Id, p6.Id, today, inst6.Amount, payerBank.Id, PaymentMethod.Ted, 0, 0, 0, null, Guid.Empty, now);
        inst6.RegisterPayment(pay6);
        p6.RecomputeSettlement();
        r6.RegisterPayment(pay6.Id, p6.Id, pay6.PrincipalApplied, c0, now);

        // #7 Triage — variety outside Tecnologia (Suporte/Canoas).
        var r7 = New(canoas, suporte, RequisitionType.ServiceProvision, "Consultoria de clima organizacional", 2_500m, EstimateType.Full, c0, vJoao);
        r7.Submit(system, now);

        // #8 Approval — variety outside Tecnologia (Marketing/Canoas).
        var r8 = New(canoas, marketing, RequisitionType.Purchase, "Impressão de materiais de captação de alunos", 9_600m, EstimateType.Full, c0, vGrafica);
        r8.Submit(system, now);
        r8.ApproveTriage(system, now);
        r8.SubmitFinancialComplement(system, now, today, Plan(9_600m, 3, PaymentMethod.Boleto));

        // Requisition graphs are new roots → EF cascades their transitions/installments/payments; the two
        // Payables cascade their installments + the settling payment.
        await db.Set<Requisition>().AddRangeAsync(new[] { r1, r2, r3, r4, r5, r6, r7, r8 }, ct);
        await db.Set<Payable>().AddRangeAsync(new[] { p5, p6 }, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seed: {Requisitions} requisitions seeded across the workflow + {Payables} requisition-sourced payables " +
            "(1 Scheduled, 1 Paid). The Approval requisition on Tecnologia/Canoas breaches the tight budget cell.",
            8, 2);
    }

    private static readonly string[] ExpenseCategoryNames =
    [
        "Energia elétrica", "Água e esgoto", "Telecom e Internet", "Aluguel e locações",
        "Manutenção predial", "Limpeza e conservação", "Segurança e vigilância",
        "Material didático e laboratório", "Acervo e assinaturas (biblioteca)",
        "Bolsas e auxílios a estudantes", "Eventos acadêmicos", "Pesquisa e extensão",
        "Folha e encargos", "Serviços de terceiros (PJ)", "Treinamento e capacitação",
        "Diárias, viagens e reembolsos", "Software e licenças", "Equipamentos de TI",
        "Cloud e hospedagem", "Marketing e captação de alunos", "Comissões e parcerias",
        "Impostos e taxas", "Tarifas bancárias", "Despesas jurídicas e cartório",
        "Seguros", "Doações e filantropia", "Outros",
    ];

    /// <summary>Staging entry point: seeds the expense categories (reference data). Idempotent.</summary>
    public static async Task SeedExpenseCategoriesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");
        await SeedExpenseCategoriesAsync(db, logger, ct);
    }

    private static async Task SeedExpenseCategoriesAsync(AppDbContext finance, ILogger logger, CancellationToken ct)
    {
        if (await finance.Set<ExpenseCategory>().AnyAsync(ct))
        {
            logger.LogInformation("Seed: expense categories already present — skipping.");
            return;
        }

        finance.Set<ExpenseCategory>().AddRange(ExpenseCategoryNames.Select(name => new ExpenseCategory(name)));
        await finance.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} expense categories seeded.", ExpenseCategoryNames.Length);
    }

    /// <summary>
    /// Seeds ONLY the global chart of accounts (LedgerAccount) — the reference data the payable
    /// classifier + posting need. No mock business data (companies/vendors/payables). Idempotent.
    /// </summary>
    public static async Task SeedLedgerChartAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");
        await SeedGlobalChartAsync(db, logger, ct);
    }

    /// <summary>Seeds the global AELBRA chart of accounts once (idempotent), returning the current chart.
    /// The chart is shared by all companies; bookkeeping stays per company via the journal.</summary>
    private static async Task<List<LedgerAccount>> SeedGlobalChartAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var accounts = await db.Set<LedgerAccount>().OrderBy(a => a.Code).ToListAsync(ct);
        if (accounts.Count > 0)
        {
            logger.LogInformation("Seed: ledger chart already present — skipping.");
            return accounts;
        }

        accounts = AelbraChart.Build().ToList();
        db.Set<LedgerAccount>().AddRange(accounts);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: global ledger chart seeded ({Count} accounts).", accounts.Count);
        return accounts;
    }

    /// <summary>
    /// Seeds ONLY the group-wide default withholding rules — the reference data the auto-withholding
    /// calculation needs. No mock business data (companies/vendors/payables). Idempotent.
    /// </summary>
    public static async Task SeedWithholdingRulesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");
        await SeedWithholdingRulesAsync(db, logger, ct);
    }

    /// <summary>Seeds group-wide default withholding rules so the auto-calculation produces output in dev/staging.
    /// Mirrors the acceptance-table set. Idempotent. Rates align with BR federal/municipal withholding rules per vendor regime
    /// (CSRF 4.65%, IRRF 1.5%, INSS 11% / 3.5% CPRB, Simples exemptions, ISS from the invoice).</summary>
    private static async Task SeedWithholdingRulesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Set<WithholdingRule>().AnyAsync(ct))
        {
            logger.LogInformation("Seed: withholding rules already present — skipping.");
            return;
        }

        var rules = new List<WithholdingRule>
        {
            // General federal taxes (any regime).
            new(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.015m),
            new(WithholdingTaxType.Pis, retains: true, RateSource.Fixed, 0.0065m),
            new(WithholdingTaxType.Cofins, retains: true, RateSource.Fixed, 0.03m),
            new(WithholdingTaxType.Csll, retains: true, RateSource.Fixed, 0.01m),
            // Simples exemption (federal income/contribution) — conditioned on the declaration on file.
            new(WithholdingTaxType.Irrf, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            new(WithholdingTaxType.Pis, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            new(WithholdingTaxType.Cofins, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            new(WithholdingTaxType.Csll, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            // INSS (social security) by the Vendor's tax regime — NOT by the LC116 sub-item: every service payable
            // withholds. General 11%; CPRB 3.5%; Simples Annex IV 11%; other Simples annexes exempt.
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m),
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, cprb: true),
            new(WithholdingTaxType.Inss, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, vendorRegime: TaxRegime.SimplesNacional, simplesAnexo: SimplesAnexo.IV),
            // ISS for Simples providers: withheld at the band rate informed on the invoice.
            new(WithholdingTaxType.Iss, retains: true, RateSource.FromNota, null, vendorRegime: TaxRegime.SimplesNacional),
        };
        db.Set<WithholdingRule>().AddRange(rules);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} withholding rules seeded.", rules.Count);
    }

    private static void ApplyStatus(Transaction t, TransactionStatus target, decimal autoApprovalLimit)
    {
        switch (target)
        {
            case TransactionStatus.Pending:
                break;
            case TransactionStatus.Approved:
                t.Approve();
                break;
            case TransactionStatus.Paid:
                t.Approve();
                t.MarkAsPaid();
                break;
            case TransactionStatus.Rejected:
                t.Reject("Rejeitado na triagem (seed de exemplo).");
                break;
        }
    }
}
