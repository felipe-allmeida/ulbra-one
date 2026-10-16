namespace Ulbra.One.Common.Application.Realtime;

/// <summary>Decide se o chamador pode ver um recurso, e portanto entrar na presença dele. Um por escopo,
/// registrado pelo módulo dono do recurso — Common não conhece módulo nenhum. Escopo sem authorizer é
/// recusado: presença revela quem está trabalhando em quê, então o padrão precisa ser fechado (§6.4).</summary>
public interface IPresenceAuthorizer
{
    /// <summary>Identificador do escopo na rota, ex.: "requisition".</summary>
    string Scope { get; }

    Task<bool> CanViewAsync(Guid resourceId, CancellationToken ct);
}
