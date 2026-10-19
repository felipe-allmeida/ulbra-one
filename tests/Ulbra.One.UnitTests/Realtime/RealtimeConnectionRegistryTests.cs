using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.UnitTests.Realtime;

public class RealtimeConnectionRegistryTests
{
    private static RealtimeEnvelope RoleSignal(string role) =>
        new(RealtimeTarget.ForRole(role), new RealtimeSignal("notification", null));

    [Fact]
    public void Sinal_de_role_chega_a_quem_tem_a_role()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), "Accounting", isAdmin: false);

        registry.Dispatch(RoleSignal("Accounting"));

        Assert.True(sub.Reader.TryRead(out var signal));
        Assert.Equal("notification", signal!.Kind);
    }

    [Fact]
    public void Sinal_de_role_nao_chega_a_outra_role()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);

        registry.Dispatch(RoleSignal("Accounting"));

        Assert.False(sub.Reader.TryRead(out _));
    }

    [Fact]
    public void Admin_recebe_sinal_de_qualquer_role()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), "Admin", isAdmin: true);

        registry.Dispatch(RoleSignal("Accounting"));

        Assert.True(sub.Reader.TryRead(out _));
    }

    [Fact]
    public void Sinal_pessoal_chega_so_ao_dono()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var owner = Guid.NewGuid();
        using var mine = registry.Subscribe(owner, "Finance", isAdmin: false);
        using var other = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);

        registry.Dispatch(new RealtimeEnvelope(
            RealtimeTarget.ForUser(owner), new RealtimeSignal("notification", null)));

        Assert.True(mine.Reader.TryRead(out _));
        Assert.False(other.Reader.TryRead(out _));
    }

    [Fact]
    public void Admin_nao_recebe_sinal_pessoal_alheio()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var admin = registry.Subscribe(Guid.NewGuid(), "Admin", isAdmin: true);

        registry.Dispatch(new RealtimeEnvelope(
            RealtimeTarget.ForUser(Guid.NewGuid()), new RealtimeSignal("notification", null)));

        Assert.False(admin.Reader.TryRead(out _));
    }

    [Fact]
    public void Sinal_de_recurso_chega_so_a_quem_esta_presente()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var resource = Guid.NewGuid();
        using var viewing = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);
        using var elsewhere = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);
        registry.EnterPresence(viewing.ConnectionId, "requisition", resource);

        registry.Dispatch(new RealtimeEnvelope(
            RealtimeTarget.ForResource("requisition", resource),
            new RealtimeSignal("presence", resource.ToString())));

        Assert.True(viewing.Reader.TryRead(out _));
        Assert.False(elsewhere.Reader.TryRead(out _));
    }

    [Fact]
    public void Canal_cheio_descarta_o_mais_antigo_sem_derrubar_a_conexao()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);

        for (var i = 0; i < RealtimeDefaults.ChannelCapacity + 10; i++)
            registry.Dispatch(new RealtimeEnvelope(
                RealtimeTarget.ForRole("Finance"), new RealtimeSignal("notification", i.ToString())));

        var drained = new List<string>();
        while (sub.Reader.TryRead(out var signal)) drained.Add(signal.ResourceId!);

        Assert.Equal(RealtimeDefaults.ChannelCapacity, drained.Count);
        Assert.Equal("10", drained.First());
        Assert.Equal("41", drained.Last());
        Assert.False(sub.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void LocalPresence_reporta_as_chaves_para_renovacao()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = registry.Subscribe(user, "Finance", isAdmin: false);
        registry.EnterPresence(sub.ConnectionId, "requisition", resource);

        Assert.Equal(
            [new PresenceKey("requisition", resource, user)],
            registry.LocalPresence());
    }

    /// <summary>
    /// A propriedade que substitui "renovação não ressuscita membro removido" (rodada 3 da revisão da
    /// Task 7 — aquele teste testava o comportamento errado, porque a correção certa NÃO é impedir a
    /// renovação de criar membro no Redis: é isto aqui. Depois de sair, a chave simplesmente não está
    /// mais em LocalPresence(), então o heartbeat (RealtimeBackplaneListener) nunca a inclui na próxima
    /// leva de RenewPresenceAsync — não tem o que renovar. A garantia mora na réplica, não no Redis.
    /// </summary>
    [Fact]
    public void Apos_sair_a_chave_nao_aparece_mais_para_renovacao()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = registry.Subscribe(user, "Finance", isAdmin: false);
        registry.EnterPresence(sub.ConnectionId, "requisition", resource);
        Assert.NotEmpty(registry.LocalPresence());

        registry.LeavePresence(sub.ConnectionId, "requisition", resource);

        Assert.Empty(registry.LocalPresence());
    }

    [Fact]
    public void Dispose_remove_a_conexao_e_sua_presenca()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var resource = Guid.NewGuid();
        var sub = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);
        registry.EnterPresence(sub.ConnectionId, "requisition", resource);

        sub.Dispose();

        Assert.Empty(registry.LocalPresence());
        registry.Dispatch(RoleSignal("Finance")); // não deve estourar
    }

    [Fact]
    public void CompleteAll_encerra_os_canais_para_o_drain()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), "Finance", isAdmin: false);

        registry.CompleteAll();

        Assert.True(sub.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void Conexao_sem_role_nao_recebe_sinal_de_role()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), role: null, isAdmin: false);

        registry.Dispatch(RoleSignal("Finance"));

        Assert.False(sub.Reader.TryRead(out _));
    }

    [Fact]
    public void Conexao_sem_role_nao_recebe_sinal_pessoal_alheio()
    {
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        using var sub = registry.Subscribe(Guid.NewGuid(), role: null, isAdmin: false);
        var otherUserId = Guid.NewGuid();

        registry.Dispatch(new RealtimeEnvelope(
            RealtimeTarget.ForUser(otherUserId), new RealtimeSignal("notification", null)));

        Assert.False(sub.Reader.TryRead(out _));
    }
}
