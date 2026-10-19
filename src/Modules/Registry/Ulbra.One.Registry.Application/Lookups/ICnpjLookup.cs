namespace Ulbra.One.Registry.Application.Lookups;

public interface ICnpjLookup
{
    Task<CnpjLookupOutcome> FindAsync(string cnpj, CancellationToken ct);
}
