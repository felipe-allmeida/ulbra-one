namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Derives the <b>tipo de serviço</b> (tpServico, Tabela 06 of EFD-Reinf — serviços prestados mediante
/// cessão de mão de obra/empreitada) from the NFS-e LC116 subitem, for the R-2010 <c>infoTpServ</c>.
/// <para>🔶 <b>DEFAULT MAP — to homologate with Accounting (AEL-627).</b> The 9-digit codes below are
/// provisional/illustrative; they (and the subitem→código mapping) must be reviewed/expanded before real fiscal
/// use. The default falls back to "outros serviços de cessão de mão de obra ou empreitada".</para></summary>
public static class TipoServicoResolver
{
    // 🔶 LC116 subitem → tpServico (Tabela 06, \d{9}). Illustrative — validate/expand with Accounting (AEL-627).
    private static readonly Dictionary<string, string> ByLc116Subitem = new()
    {
        ["11.02"] = "100000002", // vigilância ou segurança (🔶 review)
        ["7.10"] = "100000001",  // limpeza, conservação ou zeladoria (🔶 review)
        ["7.02"] = "100000003",  // construção civil (🔶 review)
        ["7.05"] = "100000003",  // construção civil (🔶 review)
        ["17.05"] = "100000031", // trabalhos administrativos / cessão de mão de obra (🔶 review)
    };

    // Fallback: "outros serviços de cessão de mão de obra ou de empreitada" (🔶 review).
    private const string DefaultTpServico = "100000032";

    public static string Resolve(string? lc116Subitem) =>
        lc116Subitem is not null && ByLc116Subitem.TryGetValue(lc116Subitem, out var code) ? code : DefaultTpServico;
}
