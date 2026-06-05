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
/// Popula o banco de DEV com dados de exemplo (empresas, unidades, centros de custo,
/// fornecedores, clientes, colaboradores e lançamentos). Idempotente: só roda se não houver
/// nenhuma empresa cadastrada. Para regerar, recrie o banco (docker compose down -v).
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

        var existing = await core.Companies.CountAsync(ct);
        if (existing > 0)
        {
            logger.LogInformation("Seed de DEV: banco já populado ({Count} empresa(s)) — pulando. Para regerar: docker compose down -v.", existing);
            return;
        }

        logger.LogInformation("Seed de DEV: banco vazio — populando dados de exemplo…");

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // ---------- Empresas (CNPJs do grupo) ----------
        var celsp = new Company("Comunidade Evangélica Luterana São Paulo - CELSP", "88332466000103");
        var ulbra = new Company("Universidade Luterana do Brasil - ULBRA", "88332466000294");
        core.Companies.AddRange(celsp, ulbra);

        // ---------- Unidades (campus/polos) da ULBRA ----------
        var canoas = new Unit(ulbra.Id, "Campus Canoas", "CAN");
        var gravatai = new Unit(ulbra.Id, "Campus Gravataí", "GRA");
        var torres = new Unit(ulbra.Id, "Campus Torres", "TOR");
        var ead = new Unit(ulbra.Id, "Educação a Distância", "EAD");
        core.Units.AddRange(canoas, gravatai, torres, ead);

        // ---------- Centros de custo (transversais às unidades) ----------
        var mkt = new CostCenter(ulbra.Id, "MKT", "Marketing e Captação", 600_000m, 5_000m);
        var ti = new CostCenter(ulbra.Id, "TI", "Tecnologia da Informação", 480_000m, 8_000m);
        var rh = new CostCenter(ulbra.Id, "RH", "Recursos Humanos", 250_000m, 3_000m);
        var infra = new CostCenter(ulbra.Id, "INFRA", "Infraestrutura e Manutenção", 900_000m, 2_000m);
        var acad = new CostCenter(ulbra.Id, "ACAD", "Acadêmico", 1_200_000m, 0m);
        var bib = new CostCenter(ulbra.Id, "BIB", "Biblioteca", 180_000m, 1_500m);
        core.CostCenters.AddRange(mkt, ti, rh, infra, acad, bib);

        // Cobertura (quais unidades cada centro de custo atende)
        core.CostCenterUnits.AddRange(
            new CostCenterUnit(mkt.Id, canoas.Id), new CostCenterUnit(mkt.Id, gravatai.Id),
            new CostCenterUnit(mkt.Id, torres.Id), new CostCenterUnit(mkt.Id, ead.Id),
            new CostCenterUnit(ti.Id, canoas.Id), new CostCenterUnit(ti.Id, ead.Id),
            new CostCenterUnit(rh.Id, canoas.Id),
            new CostCenterUnit(infra.Id, canoas.Id), new CostCenterUnit(infra.Id, gravatai.Id), new CostCenterUnit(infra.Id, torres.Id),
            new CostCenterUnit(acad.Id, canoas.Id), new CostCenterUnit(acad.Id, gravatai.Id), new CostCenterUnit(acad.Id, torres.Id), new CostCenterUnit(acad.Id, ead.Id),
            new CostCenterUnit(bib.Id, canoas.Id));

        await core.SaveChangesAsync(ct);

        // ---------- Fornecedores ----------
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

        // ---------- Clientes ----------
        var cPrefCanoas = new CustomerEntity(ulbra.Id, PersonType.Company, "Prefeitura Municipal de Canoas", "PMC",
            "88815415000100", "convenios@canoas.rs.gov.br", "5134634000", Addr("92010000", "Rua Frei Orlando", "199", "Centro", "Canoas", "RS"), 200_000m);
        var cHospital = new CustomerEntity(ulbra.Id, PersonType.Company, "Hospital Universitário de Canoas", "HU",
            "10520986000110", "financeiro@huc.com.br", "5134770000", Addr("92425900", "Av. Farroupilha", "8001", "São José", "Canoas", "RS"), 150_000m);
        var cMaria = new CustomerEntity(ulbra.Id, PersonType.Individual, "Maria Eduarda Souza", null,
            "39053344705", "maria.souza@aluno.ulbra.br", "51988887777", Addr("92020100", "Rua Santos Dumont", "55", "Centro", "Canoas", "RS"), 20_000m);
        var cCarlos = new CustomerEntity(ulbra.Id, PersonType.Individual, "Carlos Henrique Lima", null,
            "23185644780", "carlos.lima@aluno.ulbra.br", "51977776666", Addr("94010100", "Rua Dom Pedro II", "300", "Centro", "Gravataí", "RS"), 20_000m);
        registry.Customers.AddRange(cPrefCanoas, cHospital, cMaria, cCarlos);

        // ---------- Colaboradores ----------
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

        // ---------- Lançamentos (contas a pagar / receber) ----------
        // Cria com helper que aplica o status desejado de forma consistente.
        Transaction Pay(Guid unitId, CostCenter cc, string desc, string counterparty, decimal amount, int dueInDays, TransactionStatus status)
        {
            var t = new Transaction(ulbra.Id, unitId, cc.Id, TransactionType.Payable, desc, counterparty, amount, today.AddDays(dueInDays));
            ApplyStatus(t, status, cc.AutoApprovalLimit);
            return t;
        }
        Transaction Receive(Guid unitId, CostCenter cc, string desc, string counterparty, decimal amount, int dueInDays, TransactionStatus status)
        {
            var t = new Transaction(ulbra.Id, unitId, cc.Id, TransactionType.Receivable, desc, counterparty, amount, today.AddDays(dueInDays));
            ApplyStatus(t, status, cc.AutoApprovalLimit);
            return t;
        }

        finance.Transactions.AddRange(
            // A pagar — variedade de status e vencimentos
            Pay(canoas.Id, mkt, "Google Ads - campanha vestibular", "Google", 12_000m, -5, TransactionStatus.Approved),
            Pay(canoas.Id, mkt, "Impressão de materiais de captação", "Gráfica Sul", 3_200m, 7, TransactionStatus.Pending),
            Pay(canoas.Id, ti, "Licenças Microsoft 365 (anual)", "Microsoft", 96_000m, 20, TransactionStatus.Pending),
            Pay(canoas.Id, infra, "Conta de energia - Campus Canoas", "CEEE", 48_500m, -2, TransactionStatus.Approved),
            Pay(gravatai.Id, infra, "Conta de energia - Campus Gravataí", "CEEE", 21_300m, 3, TransactionStatus.Pending),
            Pay(gravatai.Id, infra, "Serviço de limpeza - mensal", "Limpa Bem", 1_800m, 10, TransactionStatus.Approved), // auto-aprovado (<= 2000)
            Pay(canoas.Id, rh, "Consultoria de clima organizacional", "João Pedro Consultoria", 2_500m, 15, TransactionStatus.Pending),
            Pay(canoas.Id, bib, "Aquisição de acervo - periódicos", "Gráfica Sul", 1_200m, -8, TransactionStatus.Paid),
            Pay(torres.Id, infra, "Reparo hidráulico - bloco B", "Limpa Bem", 5_400m, 30, TransactionStatus.Rejected),
            Pay(canoas.Id, ti, "Servidores - colocation mensal", "Microsoft", 8_900m, 12, TransactionStatus.Approved),

            // A receber
            Receive(canoas.Id, acad, "Convênio de estágios 2026/1", "Prefeitura Municipal de Canoas", 80_000m, 5, TransactionStatus.Pending),
            Receive(canoas.Id, acad, "Convênio residência médica", "Hospital Universitário de Canoas", 120_000m, -3, TransactionStatus.Approved),
            Receive(canoas.Id, acad, "Mensalidade - Medicina (Maria Eduarda)", "Maria Eduarda Souza", 6_800m, 8, TransactionStatus.Pending),
            Receive(gravatai.Id, acad, "Mensalidade - Direito (Carlos Henrique)", "Carlos Henrique Lima", 2_400m, -1, TransactionStatus.Paid),
            Receive(ead.Id, acad, "Mensalidade EAD - turma Pedagogia", "Maria Eduarda Souza", 980m, 18, TransactionStatus.Pending));

        await finance.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seed de DEV concluído: {Companies} empresas, {Units} unidades, {CostCenters} centros de custo, " +
            "{Vendors} fornecedores, {Customers} clientes, {Employees} colaboradores, {Transactions} lançamentos.",
            2, 4, 6, 6, 4, 5, 15);
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
