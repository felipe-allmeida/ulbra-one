namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Derives the <b>natureza do rendimento</b> (natRend, Tabela 01 of EFD-Reinf) from the NFS-e LC116
/// subitem, with a fallback by guia de recolhimento.
/// <para>🔶 <b>DEFAULT MAP — to homologate with Accounting (AEL-627).</b> The codes below are
/// provisional/illustrative; they must be reviewed/expanded before real fiscal use.</para></summary>
public static class IncomeNatureResolver
{
    // 🔶 LC116 subitem → natRend (Tabela 01). Illustrative — validate/expand with Accounting (AEL-627).
    private static readonly Dictionary<string, string> ByLc116Subitem = new()
    {
        ["17.01"] = "10002", // assessoria/consultoria (🔶 review)
        ["1.07"] = "13001",  // suporte técnico em TI (🔶 review)
    };

    // Fallback when no subitem is mapped — serviços em geral (🔶 review).
    private const string DefaultNatRend = "15009";

    public static string Resolve(string? lc116Subitem, RetentionGroup group) =>
        lc116Subitem is not null && ByLc116Subitem.TryGetValue(lc116Subitem, out var code) ? code : DefaultNatRend;
}
