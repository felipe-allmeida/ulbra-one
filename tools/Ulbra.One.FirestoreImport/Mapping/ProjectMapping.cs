namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Maps a legacy `projetos` document status onto the One's ProjectStatus (enum-name string).</summary>
public static class ProjectMapping
{
    public static string MapStatus(string? legacy) => (legacy ?? string.Empty).Trim() switch
    {
        "Finalizado" => "Finished",
        "Cancelado" => "Cancelled",
        _ => "Active", // Ativo / unknown
    };
}
