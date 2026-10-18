using System.Globalization;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>A resolved unit: its own id plus the company it belongs to.</summary>
public readonly record struct UnitRef(Guid UnitId, Guid CompanyId);

/// <summary>In-memory snapshot of the target org used to resolve legacy references at import time,
/// loaded once per run from the target Postgres. Cost centers are matched PER COMPANY by a normalized
/// natureza key (the same name, e.g. "Ensino", exists under several companies; only <c>(company_id,
/// code)</c> is unique). Also PROVISIONS a missing natureza on demand under the company that needs it
/// (<see cref="EnsureCostCenterAsync"/>) — the legacy naturezas are flat, the One's are company-scoped,
/// so a budget/user that references a natureza absent from its company gets it created there rather than
/// dead-lettered.</summary>
public sealed class OrgResolver
{
    /// <summary>Ulbra S.A — owns the corporate naturezas/unit codes/budgets. No fixed GUID; resolved by CNPJ.</summary>
    public const string UlbraSaCnpj = "88332466000294";
    private const string Actor = "firestore-migration";

    private readonly NpgsqlDataSource _dataSource;
    private readonly Dictionary<string, UnitRef> _unitsByCode;
    private readonly Dictionary<(Guid Company, string NatureKey), Guid> _ccByCompanyNature;
    private readonly Dictionary<Guid, List<Guid>> _ccByCompany;
    private readonly Dictionary<Guid, HashSet<string>> _ccCodesByCompany;
    private readonly Dictionary<string, Guid> _companiesByCnpj;
    private readonly Dictionary<string, Guid> _vendorsByDocument;
    private readonly Dictionary<string, Guid> _usersByEmail;
    private readonly HashSet<(Guid CostCenter, Guid Unit)> _coverage;

    private OrgResolver(
        NpgsqlDataSource dataSource,
        Dictionary<string, UnitRef> unitsByCode,
        Dictionary<(Guid, string), Guid> ccByCompanyNature,
        Dictionary<Guid, List<Guid>> ccByCompany,
        Dictionary<Guid, HashSet<string>> ccCodesByCompany,
        Dictionary<string, Guid> companiesByCnpj,
        Dictionary<string, Guid> vendorsByDocument,
        Dictionary<string, Guid> usersByEmail,
        HashSet<(Guid, Guid)> coverage)
    {
        _dataSource = dataSource;
        _unitsByCode = unitsByCode;
        _ccByCompanyNature = ccByCompanyNature;
        _ccByCompany = ccByCompany;
        _ccCodesByCompany = ccCodesByCompany;
        _companiesByCnpj = companiesByCnpj;
        _vendorsByDocument = vendorsByDocument;
        _usersByEmail = usersByEmail;
        _coverage = coverage;
        PrimaryCompanyId = companiesByCnpj.TryGetValue(UlbraSaCnpj, out var id) ? id : null;
    }

    /// <summary>The Ulbra S.A company id, or null if it is not seeded in the target.</summary>
    public Guid? PrimaryCompanyId { get; }

    /// <summary>All resolvable units — used to fan out a user's <c>["all"]</c> unit scope.</summary>
    public IReadOnlyCollection<UnitRef> AllUnits => _unitsByCode.Values;

    /// <summary>Cost centers belonging to a company — used to fan out a user's <c>["all"]</c> nature scope.</summary>
    public IReadOnlyList<Guid> CostCentersOfCompany(Guid companyId)
        => _ccByCompany.TryGetValue(companyId, out var list) ? list : [];

    public static async Task<OrgResolver> LoadAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var companies = await conn.QueryAsync<CompanyRow>(new CommandDefinition(
            "select id as Id, cnpj as Cnpj from core.companies where deleted_at is null",
            cancellationToken: ct));
        var units = await conn.QueryAsync<UnitRow>(new CommandDefinition(
            "select id as Id, company_id as CompanyId, code as Code from core.units where active and code is not null",
            cancellationToken: ct));
        var costCenters = await conn.QueryAsync<CcRow>(new CommandDefinition(
            "select id as Id, company_id as CompanyId, code as Code, name as Name from core.cost_centers where active",
            cancellationToken: ct));
        var coverageRows = await conn.QueryAsync<CoverageRow>(new CommandDefinition(
            "select cost_center_id as CostCenterId, unit_id as UnitId from core.cost_center_units",
            cancellationToken: ct));
        var vendors = await conn.QueryAsync<VendorRow>(new CommandDefinition(
            "select id as Id, document as Document from registry.vendors where deleted_at is null",
            cancellationToken: ct));
        var users = await conn.QueryAsync<UserEmailRow>(new CommandDefinition(
            "select id as Id, email as Email from identity.users", cancellationToken: ct));

        var unitsByCode = units.ToDictionary(
            u => u.Code, u => new UnitRef(u.Id, u.CompanyId), StringComparer.OrdinalIgnoreCase);

        var ccByCompanyNature = new Dictionary<(Guid, string), Guid>();
        var ccByCompany = new Dictionary<Guid, List<Guid>>();
        var ccCodesByCompany = new Dictionary<Guid, HashSet<string>>();
        foreach (var cc in costCenters)
        {
            ccByCompanyNature[(cc.CompanyId, NatureKey.Of(cc.Name))] = cc.Id;
            (ccByCompany.TryGetValue(cc.CompanyId, out var list) ? list : ccByCompany[cc.CompanyId] = []).Add(cc.Id);
            (ccCodesByCompany.TryGetValue(cc.CompanyId, out var codes)
                ? codes
                : ccCodesByCompany[cc.CompanyId] = new(StringComparer.OrdinalIgnoreCase)).Add(cc.Code);
        }

        var companiesByCnpj = companies.ToDictionary(c => c.Cnpj, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var coverage = coverageRows.Select(r => (r.CostCenterId, r.UnitId)).ToHashSet();

        var vendorsByDocument = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in vendors)
            vendorsByDocument[v.Document] = v.Id;

        var usersByEmail = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
            if (!string.IsNullOrWhiteSpace(u.Email))
                usersByEmail[u.Email.Trim()] = u.Id;

        return new OrgResolver(
            dataSource, unitsByCode, ccByCompanyNature, ccByCompany, ccCodesByCompany,
            companiesByCnpj, vendorsByDocument, usersByEmail, coverage);
    }

    public bool TryResolveVendor(string? document, out Guid vendorId)
        => _vendorsByDocument.TryGetValue((document ?? string.Empty).Trim(), out vendorId);

    public bool TryResolveUnit(string? code, out UnitRef unit)
        => _unitsByCode.TryGetValue((code ?? string.Empty).Trim(), out unit);

    /// <summary>Resolves a natureza to a cost center within a specific company (read-only).</summary>
    public bool TryResolveCostCenter(Guid companyId, string? natureName, out Guid costCenterId)
        => _ccByCompanyNature.TryGetValue((companyId, NatureKey.Of(natureName)), out costCenterId);

    public bool TryResolveCompanyByCnpj(string cnpj, out Guid companyId)
        => _companiesByCnpj.TryGetValue(cnpj, out companyId);

    /// <summary>Resolves a user by e-mail (unique) to their One id — the adequação requester becomes
    /// created_by/updated_by and the transition actor.</summary>
    public bool TryResolveUserByEmail(string? email, out Guid userId)
        => _usersByEmail.TryGetValue((email ?? string.Empty).Trim(), out userId);

    public bool HasCoverage(Guid costCenterId, Guid unitId)
        => _coverage.Contains((costCenterId, unitId));

    /// <summary>Returns the cost center for a natureza in a company, CREATING it there if absent (with a
    /// synthesized unique code, name in the One's title-case style). Persisted on its own connection so
    /// the org completion survives regardless of the calling row's outcome; the in-memory cache is
    /// updated so the same run reuses it.</summary>
    public async Task<Guid> EnsureCostCenterAsync(Guid companyId, string? natureName, CancellationToken ct)
    {
        if (TryResolveCostCenter(companyId, natureName, out var existing))
            return existing;

        var name = (natureName ?? string.Empty).Trim();
        var code = SynthesizeCode(companyId, name);
        var newId = Guid.NewGuid();

        const string sql = """
            insert into core.cost_centers (
                id, company_id, code, name, auto_approval_limit, active,
                created_at, created_by, updated_at, updated_by)
            values (@newId, @companyId, @code, @displayName, 0, true, now(), @actor, now(), @actor)
            on conflict (company_id, code) do nothing
            returning id
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var returned = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            sql, new { newId, companyId, code, displayName = TitleCase(name), actor = Actor }, cancellationToken: ct));

        var finalId = returned ?? await conn.QuerySingleAsync<Guid>(new CommandDefinition(
            "select id from core.cost_centers where company_id = @companyId and code = @code",
            new { companyId, code }, cancellationToken: ct));

        _ccByCompanyNature[(companyId, NatureKey.Of(name))] = finalId;
        (_ccByCompany.TryGetValue(companyId, out var list) ? list : _ccByCompany[companyId] = []).Add(finalId);
        (_ccCodesByCompany.TryGetValue(companyId, out var codes)
            ? codes
            : _ccCodesByCompany[companyId] = new(StringComparer.OrdinalIgnoreCase)).Add(code);

        return finalId;
    }

    // Deterministic, unique-per-company code from the natureza name: word initials, then a numeric
    // suffix on collision. Stable across runs because an existing natureza is matched by name (never re-created).
    private string SynthesizeCode(Guid companyId, string natureName)
    {
        var key = NatureKey.Of(natureName);
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var acronym = new string([.. words.Select(w => w[0]).Where(char.IsLetterOrDigit).Take(6)]);
        if (acronym.Length < 2)
            acronym = new string([.. key.Where(char.IsLetterOrDigit).Take(4)]);
        if (acronym.Length == 0)
            acronym = "CC";

        var taken = _ccCodesByCompany.TryGetValue(companyId, out var set) ? set : [];
        var candidate = acronym;
        var n = 1;
        while (taken.Contains(candidate))
            candidate = $"{acronym}{n++}";

        return candidate.Length <= 50 ? candidate : candidate[..50];
    }

    private static string TitleCase(string value)
        => value.Length == 0 ? value : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private sealed record CompanyRow(Guid Id, string Cnpj);
    private sealed record VendorRow(Guid Id, string Document);
    private sealed record UserEmailRow(Guid Id, string? Email);
    private sealed record UnitRow(Guid Id, Guid CompanyId, string Code);
    private sealed record CcRow(Guid Id, Guid CompanyId, string Code, string Name);
    private sealed record CoverageRow(Guid CostCenterId, Guid UnitId);
}
