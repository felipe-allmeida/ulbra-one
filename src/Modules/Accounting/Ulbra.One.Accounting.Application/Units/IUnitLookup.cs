namespace Ulbra.One.Accounting.Application.Units;

/// <summary>Minimal projection of a Core unit (estabelecimento) used for cross-module validation and report
/// labels. Accounting references Core only by loose <c>Guid</c>; the implementation lives in Infrastructure
/// (which may reference Core.Domain), keeping the Application layer free of a Core dependency.</summary>
public sealed record UnitInfo(Guid Id, Guid CompanyId, bool Active, string Name, string Acronym);

/// <summary>Port that resolves units (Core module) by id — for the journal-line guard and report labels.</summary>
public interface IUnitLookup
{
    /// <summary>Returns the requested units keyed by id; ids that don't exist are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, UnitInfo>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
