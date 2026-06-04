namespace Ulbra.One.Registry.Infrastructure.Lookups;

public interface ICepLookup
{
    Task<CepResult?> FindAsync(string cep, CancellationToken ct);
}
