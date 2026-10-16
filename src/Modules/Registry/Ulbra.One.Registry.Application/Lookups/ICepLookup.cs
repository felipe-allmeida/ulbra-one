namespace Ulbra.One.Registry.Application.Lookups;

public interface ICepLookup
{
    Task<CepResult?> FindAsync(string cep, CancellationToken ct);
}
