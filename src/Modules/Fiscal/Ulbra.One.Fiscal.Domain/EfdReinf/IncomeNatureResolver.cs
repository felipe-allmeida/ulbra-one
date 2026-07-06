namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Deriva a <b>natureza do rendimento</b> (natRend, Tabela 01 da EFD-Reinf) do subitem LC116 da NFS-e,
/// com fallback por guia de recolhimento.
/// <para>🔶 <b>MAPA DEFAULT — a homologar com a Contabilidade (AEL-627).</b> Os códigos abaixo são
/// provisórios/ilustrativos; devem ser revisados/expandidos antes do uso fiscal real.</para></summary>
public static class IncomeNatureResolver
{
    // 🔶 LC116 subitem → natRend (Tabela 01). Ilustrativo — validar/expandir com a Contabilidade (AEL-627).
    private static readonly Dictionary<string, string> ByLc116Subitem = new()
    {
        ["17.01"] = "10002", // assessoria/consultoria (🔶 revisar)
        ["1.07"] = "13001",  // suporte técnico em TI (🔶 revisar)
    };

    // Fallback quando não há subitem mapeado — serviços em geral (🔶 revisar).
    private const string DefaultNatRend = "15009";

    public static string Resolve(string? lc116Subitem, RetentionGroup group) =>
        lc116Subitem is not null && ByLc116Subitem.TryGetValue(lc116Subitem, out var code) ? code : DefaultNatRend;
}
