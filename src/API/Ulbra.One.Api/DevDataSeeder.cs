using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Database;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;
using Ulbra.One.Registry.Infrastructure.Database;

namespace Ulbra.One.Api;

/// <summary>
/// Populates the DEV database with sample data (companies, units, cost centers,
/// vendors, customers, employees and transactions). Idempotent: only runs if no
/// company exists. To reseed, recreate the database (docker compose down -v).
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var core = sp.GetRequiredService<CoreContext>();
        var finance = sp.GetRequiredService<FinanceContext>();
        var registry = sp.GetRequiredService<RegistryContext>();
        var time = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        await SeedExpenseCategoriesAsync(finance, logger, ct);

        var existing = await core.Companies.CountAsync(ct);
        if (existing > 0)
        {
            logger.LogInformation("Dev seed: database already populated ({Count} company(ies)) — skipping. To reseed: docker compose down -v.", existing);
            return;
        }

        logger.LogInformation("Dev seed: database is empty — seeding sample data…");

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // ---------- Companies (group CNPJs) ----------
        var celsp = new Company("Comunidade Evangélica Luterana São Paulo - CELSP", "88332466000103");
        var ulbra = new Company("Universidade Luterana do Brasil - ULBRA", "88332466000294");
        core.Companies.AddRange(celsp, ulbra);

        // ---------- Units (ULBRA campuses/poles) ----------
        var canoas = new Unit(ulbra.Id, "Campus Canoas", "CAN");
        var gravatai = new Unit(ulbra.Id, "Campus Gravataí", "GRA");
        var torres = new Unit(ulbra.Id, "Campus Torres", "TOR");
        var ead = new Unit(ulbra.Id, "Educação a Distância", "EAD");
        core.Units.AddRange(canoas, gravatai, torres, ead);

        // ---------- Cost centers (cross-cutting across units) ----------
        var mkt = new CostCenter(ulbra.Id, "MKT", "Marketing e Captação", 600_000m, 5_000m);
        var ti = new CostCenter(ulbra.Id, "TI", "Tecnologia da Informação", 480_000m, 8_000m);
        var rh = new CostCenter(ulbra.Id, "RH", "Recursos Humanos", 250_000m, 3_000m);
        var infra = new CostCenter(ulbra.Id, "INFRA", "Infraestrutura e Manutenção", 900_000m, 2_000m);
        var acad = new CostCenter(ulbra.Id, "ACAD", "Acadêmico", 1_200_000m, 0m);
        var bib = new CostCenter(ulbra.Id, "BIB", "Biblioteca", 180_000m, 1_500m);
        core.CostCenters.AddRange(mkt, ti, rh, infra, acad, bib);

        // Coverage (which units each cost center serves)
        core.CostCenterUnits.AddRange(
            new CostCenterUnit(mkt.Id, canoas.Id), new CostCenterUnit(mkt.Id, gravatai.Id),
            new CostCenterUnit(mkt.Id, torres.Id), new CostCenterUnit(mkt.Id, ead.Id),
            new CostCenterUnit(ti.Id, canoas.Id), new CostCenterUnit(ti.Id, ead.Id),
            new CostCenterUnit(rh.Id, canoas.Id),
            new CostCenterUnit(infra.Id, canoas.Id), new CostCenterUnit(infra.Id, gravatai.Id), new CostCenterUnit(infra.Id, torres.Id),
            new CostCenterUnit(acad.Id, canoas.Id), new CostCenterUnit(acad.Id, gravatai.Id), new CostCenterUnit(acad.Id, torres.Id), new CostCenterUnit(acad.Id, ead.Id),
            new CostCenterUnit(bib.Id, canoas.Id));

        await core.SaveChangesAsync(ct);

        // ---------- Bank accounts (outflow accounts for payables) ----------
        if (!await finance.BankAccounts.AnyAsync(ct))
        {
            finance.BankAccounts.AddRange(
                new BankAccount(ulbra.Id, "033", "Banco Santander", "1234", "00012345", "6", BankAccountType.Checking, "Santander - Conta Movimento", 250_000m),
                new BankAccount(ulbra.Id, "001", "Banco do Brasil", "5678", "00098765", "4", BankAccountType.Checking, "Banco do Brasil - Conta Principal", 180_000m),
                new BankAccount(celsp.Id, "033", "Banco Santander", "4321", "00054321", "0", BankAccountType.Checking, "Santander - CELSP", 60_000m));
            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Dev seed: 3 bank accounts seeded.");
        }

        // ---------- Vendors ----------
        Address Addr(string cep, string street, string number, string bairro, string city, string uf) =>
            new(cep, street, number, null, bairro, city, uf);

        var vGoogle = new VendorEntity(ulbra.Id, PersonType.Company, "Google Brasil Internet Ltda.", "Google",
            "06990590000123", "ads@google.com", "1140003000", Addr("04538133", "Av. Brigadeiro Faria Lima", "3477", "Itaim Bibi", "São Paulo", "SP"),
            "30 dias", "Banco 341 / Ag 0001 / CC 12345-6");
        var vCeee = new VendorEntity(ulbra.Id, PersonType.Company, "CEEE Equatorial Energia S.A.", "CEEE",
            "08467115000100", "atendimento@ceee.com.br", "5132000000", Addr("90230000", "Rua dos Andradas", "1000", "Centro", "Porto Alegre", "RS"),
            "à vista", null);
        var vMicrosoft = new VendorEntity(ulbra.Id, PersonType.Company, "Microsoft Informática Ltda.", "Microsoft",
            "60316817000103", "licen@microsoft.com", "1150004000", Addr("04711130", "Av. das Nações Unidas", "12901", "Brooklin", "São Paulo", "SP"),
            "anual", null);
        var vGrafica = new VendorEntity(ulbra.Id, PersonType.Company, "Gráfica Sul Impressões Ltda.", "Gráfica Sul",
            "92749225000180", "comercial@graficasul.com.br", "5133334444", Addr("92020000", "Rua Marechal Floriano", "250", "Centro", "Canoas", "RS"),
            "28 dias", null);
        var vLimpeza = new VendorEntity(ulbra.Id, PersonType.Company, "Limpa Bem Serviços Ltda.", "Limpa Bem",
            "11444777000161", "contato@limpabem.com.br", "5133335555", Addr("94010000", "Av. Getúlio Vargas", "800", "Centro", "Gravataí", "RS"),
            "30 dias", null);
        var vJoao = new VendorEntity(ulbra.Id, PersonType.Individual, "João Pedro Consultoria", null,
            "52998224725", "joao.consultor@gmail.com", "51999990000", Addr("90560000", "Rua Mostardeiro", "100", "Moinhos de Vento", "Porto Alegre", "RS"),
            "à vista", null);
        registry.Vendors.AddRange(vGoogle, vCeee, vMicrosoft, vGrafica, vLimpeza, vJoao);

        // ---------- Customers ----------
        var cPrefCanoas = new CustomerEntity(ulbra.Id, PersonType.Company, "Prefeitura Municipal de Canoas", "PMC",
            "88815415000100", "convenios@canoas.rs.gov.br", "5134634000", Addr("92010000", "Rua Frei Orlando", "199", "Centro", "Canoas", "RS"), 200_000m);
        var cHospital = new CustomerEntity(ulbra.Id, PersonType.Company, "Hospital Universitário de Canoas", "HU",
            "10520986000110", "financeiro@huc.com.br", "5134770000", Addr("92425900", "Av. Farroupilha", "8001", "São José", "Canoas", "RS"), 150_000m);
        var cMaria = new CustomerEntity(ulbra.Id, PersonType.Individual, "Maria Eduarda Souza", null,
            "39053344705", "maria.souza@aluno.ulbra.br", "51988887777", Addr("92020100", "Rua Santos Dumont", "55", "Centro", "Canoas", "RS"), 20_000m);
        var cCarlos = new CustomerEntity(ulbra.Id, PersonType.Individual, "Carlos Henrique Lima", null,
            "23185644780", "carlos.lima@aluno.ulbra.br", "51977776666", Addr("94010100", "Rua Dom Pedro II", "300", "Centro", "Gravataí", "RS"), 20_000m);
        registry.Customers.AddRange(cPrefCanoas, cHospital, cMaria, cCarlos);

        // ---------- Employees ----------
        registry.Employees.AddRange(
            new EmployeeEntity(ulbra.Id, "Ana Beatriz Ferreira", "11144477735", "ana.ferreira@ulbra.br", "51999991111",
                "Analista de Marketing", canoas.Id, mkt.Id, today.AddYears(-2), Addr("92020200", "Rua Tiradentes", "120", "Centro", "Canoas", "RS")),
            new EmployeeEntity(ulbra.Id, "Bruno Carvalho", "30880150652", "bruno.carvalho@ulbra.br", "51999992222",
                "Coordenador de TI", canoas.Id, ti.Id, today.AddYears(-4), Addr("90040000", "Av. Ipiranga", "6681", "Partenon", "Porto Alegre", "RS")),
            new EmployeeEntity(ulbra.Id, "Carla Menezes", "64426828530", "carla.menezes@ulbra.br", "51999993333",
                "Analista de RH", canoas.Id, rh.Id, today.AddYears(-1), Addr("92010100", "Rua Quinze de Janeiro", "40", "Centro", "Canoas", "RS")),
            new EmployeeEntity(ulbra.Id, "Diego Rodrigues", "85245484300", "diego.rodrigues@ulbra.br", "51999994444",
                "Técnico de Infraestrutura", gravatai.Id, infra.Id, today.AddYears(-3), Addr("94010200", "Rua Júlio de Castilhos", "75", "Centro", "Gravataí", "RS")),
            new EmployeeEntity(ulbra.Id, "Eduarda Pires", "29537995890", "eduarda.pires@ulbra.br", "51999995555",
                "Bibliotecária", canoas.Id, bib.Id, today.AddMonths(-8), Addr("92020300", "Rua General Câmara", "200", "Centro", "Canoas", "RS")));

        await registry.SaveChangesAsync(ct);

        // ---------- Payables (accounts-payable aggregate) ----------
        // Resolve a few categories by name to link payables to.
        var categories = await finance.ExpenseCategories.ToListAsync(ct);
        ExpenseCategory? Cat(string name) => categories.FirstOrDefault(c => c.Name == name);

        Payable PayableVista(CostCenter cc, string desc, VendorEntity vendor, ExpenseCategory? category,
            string? documentNumber, decimal amount, int dueInDays)
        {
            var installments = new List<Installment> { new(1, today.AddDays(dueInDays), amount) };
            var p = new Payable(ulbra.Id, PayableOrigin.Manual, vendor.Id, vendor.Name, documentNumber,
                today, desc, cc.Id, category?.Id, installments);
            p.ApplyAutoApproval(cc.AutoApprovalLimit);
            return p;
        }
        Payable PayableParcelado(CostCenter cc, string desc, VendorEntity vendor, ExpenseCategory? category,
            string? documentNumber, decimal total, int count, int firstDueInDays, int intervalMonths = 1)
        {
            var installments = Payable.GenerateInstallments(total, count, today.AddDays(firstDueInDays), intervalMonths);
            var p = new Payable(ulbra.Id, PayableOrigin.Manual, vendor.Id, vendor.Name, documentNumber,
                today, desc, cc.Id, category?.Id, installments);
            p.ApplyAutoApproval(cc.AutoApprovalLimit);
            return p;
        }

        var pLimpeza = PayableVista(infra, "Serviço de limpeza - mensal", vLimpeza, Cat("Limpeza e conservação"), "NF-LB-0098", 1_800m, 10); // auto-approved (<= 2000)
        var pGoogle = PayableVista(mkt, "Google Ads - campanha vestibular", vGoogle, Cat("Marketing e captação de alunos"), "NF-2026-0451", 12_000m, -5);
        if (pGoogle.Status != PayableStatus.Approved) pGoogle.Approve();

        await finance.Payables.AddRangeAsync(new[]
        {
            // À vista
            pGoogle,
            PayableVista(infra, "Conta de energia - Campus Canoas", vCeee, Cat("Energia elétrica"), "NF-CEEE-77120", 48_500m, -2),
            PayableVista(infra, "Conta de energia - Campus Gravataí", vCeee, Cat("Energia elétrica"), "NF-CEEE-77121", 21_300m, 3),
            pLimpeza,
            PayableVista(rh, "Consultoria de clima organizacional", vJoao, Cat("Serviços de terceiros (PJ)"), null, 2_500m, 15),
            PayableVista(bib, "Aquisição de acervo - periódicos", vGrafica, Cat("Acervo e assinaturas (biblioteca)"), "NF-GS-1207", 1_200m, -8),
        }, ct);

        await finance.Payables.AddRangeAsync(new[]
        {
            // Parcelado
            PayableParcelado(ti, "Licenças Microsoft 365 (anual)", vMicrosoft, Cat("Software e licenças"), "NF-MS-330021", 96_000m, 12, 20),
            PayableParcelado(mkt, "Impressão de materiais de captação", vGrafica, Cat("Marketing e captação de alunos"), "NF-GS-1233", 9_600m, 3, 7),
            PayableParcelado(ti, "Servidores - colocation", vMicrosoft, Cat("Cloud e hospedagem"), "NF-MS-330044", 26_700m, 3, 12),
        }, ct);

        // ---------- Receivables (kept on Transaction) ----------
        Transaction Receive(Guid unitId, CostCenter cc, string desc, CustomerEntity customer, decimal amount, int dueInDays, TransactionStatus status)
        {
            var t = new Transaction(ulbra.Id, unitId, cc.Id, TransactionType.Receivable, desc, null, customer.Id, customer.Name, amount, today.AddDays(dueInDays));
            ApplyStatus(t, status, cc.AutoApprovalLimit);
            return t;
        }

        finance.Transactions.AddRange(
            Receive(canoas.Id, acad, "Convênio de estágios 2026/1", cPrefCanoas, 80_000m, 5, TransactionStatus.Pending),
            Receive(canoas.Id, acad, "Convênio residência médica", cHospital, 120_000m, -3, TransactionStatus.Approved),
            Receive(canoas.Id, acad, "Mensalidade - Medicina (Maria Eduarda)", cMaria, 6_800m, 8, TransactionStatus.Pending),
            Receive(gravatai.Id, acad, "Mensalidade - Direito (Carlos Henrique)", cCarlos, 2_400m, -1, TransactionStatus.Paid),
            Receive(ead.Id, acad, "Mensalidade EAD - turma Pedagogia", cMaria, 980m, 18, TransactionStatus.Pending));

        await finance.SaveChangesAsync(ct);

        // ---------- Payments (baixa) — show settled & partially-paid states in the UI ----------
        if (!await finance.Payments.AnyAsync(ct))
        {
            var ulbraBank = await finance.BankAccounts.FirstAsync(b => b.CompanyId == ulbra.Id, ct);
            var now = time.GetUtcNow();
            var paidOn = today.AddDays(-1);

            // Full payment → settles the Google Ads payable.
            var fullInstallment = pGoogle.Installments[0];
            var fullPayment = new Payment(fullInstallment.Id, pGoogle.Id, paidOn, fullInstallment.Amount,
                ulbraBank.Id, PaymentMethod.Pix, 0, 0, 0, null, Guid.Empty, now);
            fullInstallment.RegisterPayment(fullPayment);
            pGoogle.RecomputeSettlement();
            await finance.Payments.AddAsync(fullPayment, ct);

            // Partial payment → leaves the limpeza payable partially paid.
            var partialInstallment = pLimpeza.Installments[0];
            var partialPayment = new Payment(partialInstallment.Id, pLimpeza.Id, paidOn,
                Math.Round(partialInstallment.Amount / 2, 2), ulbraBank.Id, PaymentMethod.Boleto, 0, 0, 0, null, Guid.Empty, now);
            partialInstallment.RegisterPayment(partialPayment);
            pLimpeza.RecomputeSettlement();
            await finance.Payments.AddAsync(partialPayment, ct);

            await finance.SaveChangesAsync(ct);
            logger.LogInformation("Dev seed: 2 payments seeded (1 full/settled, 1 partial).");
        }

        logger.LogInformation(
            "Dev seed complete: {Companies} companies, {Units} units, {CostCenters} cost centers, " +
            "{Vendors} vendors, {Customers} customers, {Employees} employees, {Payables} payables, {Receivables} receivables.",
            2, 4, 6, 6, 4, 5, 9, 5);
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

    private static async Task SeedExpenseCategoriesAsync(FinanceContext finance, ILogger logger, CancellationToken ct)
    {
        if (await finance.ExpenseCategories.AnyAsync(ct))
        {
            logger.LogInformation("Dev seed: expense categories already present — skipping.");
            return;
        }

        finance.ExpenseCategories.AddRange(ExpenseCategoryNames.Select(name => new ExpenseCategory(name)));
        await finance.SaveChangesAsync(ct);
        logger.LogInformation("Dev seed: {Count} expense categories seeded.", ExpenseCategoryNames.Length);
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
