using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Infrastructure.Realtime;

namespace Ulbra.One.UnitTests.Realtime;

/// <summary>
/// Regressão da revisão da Task 7 (CRITICAL 2): em prod, api_max_replicas=2 sem afinidade de sessão, então
/// a conexão SSE de um usuário e a chamada POST/DELETE /presence dele podem cair em réplicas diferentes.
/// Antes desta correção, EnterPresence/LeavePresence mutavam <see cref="RealtimeConnectionRegistry"/>
/// direto — um no-op silencioso quando a conexão não é local — então a presença nunca era renovada
/// (RealtimeBackplaneListener só renova LocalPresence()) e o sinal de "presença mudou" nunca alcançava
/// essa conexão (Dispatch casa por Presence local). O usuário sumia da tela dos outros em até 45s, e
/// nunca via ninguém entrar/sair.
/// <para>
/// Este teste simula duas réplicas com dois <see cref="RealtimeConnectionRegistry"/> independentes
/// compartilhando o mesmo backplane (o mesmo fan-out que StreamRealtime já usa para sinais normais,
/// verificado ao vivo na Task 4) — exatamente como EnterPresence/LeavePresence devem rotear o comando
/// agora: publicado uma vez, aplicado só onde a conexão realmente mora.
/// </para>
/// </summary>
public class PresenceReplicaRoutingTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static async Task<(InMemoryRealtimeBackplane Backplane, RealtimeConnectionRegistry ReplicaA, RealtimeConnectionRegistry ReplicaB)>
        TwoReplicasAsync()
    {
        var backplane = new InMemoryRealtimeBackplane(Clock);
        var replicaA = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var replicaB = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        await backplane.SubscribeAsync(e => { replicaA.Dispatch(e); return Task.CompletedTask; }, CancellationToken.None);
        await backplane.SubscribeAsync(e => { replicaB.Dispatch(e); return Task.CompletedTask; }, CancellationToken.None);
        return (backplane, replicaA, replicaB);
    }

    [Fact]
    public async Task Comando_de_entrar_pousa_so_na_replica_dona_da_conexao()
    {
        var (backplane, replicaA, replicaB) = await TwoReplicasAsync();
        var userId = Guid.NewGuid();
        var resource = Guid.NewGuid();
        // O stream SSE do usuário vive na réplica A.
        using var sub = replicaA.Subscribe(userId, "Requester", isAdmin: false);

        // POST /presence caiu na réplica B (a que recebeu a chamada não é a dona da conexão) — é isso
        // que endpoints/EnterPresence.cs faz agora: publica no backplane em vez de mutar B.registry direto.
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, userId, "requisition", resource, seq: 1),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), CancellationToken.None);

        Assert.Equal([new PresenceKey("requisition", resource, userId)], replicaA.LocalPresence());
        Assert.Empty(replicaB.LocalPresence());
    }

    [Fact]
    public async Task Comando_de_entrar_nao_vaza_como_sinal_para_o_proprio_cliente()
    {
        // O comando de presença é interno entre réplicas — nunca deve aparecer no stream SSE do próprio
        // cliente como se fosse um RealtimeSignal comum.
        var (backplane, replicaA, _) = await TwoReplicasAsync();
        var userId = Guid.NewGuid();
        using var sub = replicaA.Subscribe(userId, "Requester", isAdmin: false);

        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, userId, "requisition", Guid.NewGuid(), seq: 1),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), CancellationToken.None);

        Assert.False(sub.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Comando_de_sair_remove_so_na_replica_dona_da_conexao()
    {
        var (backplane, replicaA, replicaB) = await TwoReplicasAsync();
        var userId = Guid.NewGuid();
        var resource = Guid.NewGuid();
        using var sub = replicaA.Subscribe(userId, "Requester", isAdmin: false);
        replicaA.EnterPresence(sub.ConnectionId, "requisition", resource);

        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, userId, "requisition", resource, seq: 1),
            new RealtimeSignal(PresenceCommandKind.Leave, null)), CancellationToken.None);

        Assert.Empty(replicaA.LocalPresence());
    }

    [Fact]
    public async Task Comando_com_userId_diferente_do_dono_da_conexao_e_recusado()
    {
        // Guarda contra spoofing (IMPORTANT 2 da revisão): um connectionId de outro usuário não pode
        // injetar presença nele. Só a réplica dona sabe de quem é a conexão, e é ela que recusa.
        var (backplane, replicaA, _) = await TwoReplicasAsync();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var resource = Guid.NewGuid();
        using var sub = replicaA.Subscribe(owner, "Requester", isAdmin: false);

        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, attacker, "requisition", resource, seq: 1),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), CancellationToken.None);

        Assert.Empty(replicaA.LocalPresence());
    }

    [Fact]
    public async Task Comando_para_conexao_inexistente_e_ignorado_sem_estourar()
    {
        var (backplane, replicaA, replicaB) = await TwoReplicasAsync();

        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(Guid.NewGuid(), Guid.NewGuid(), "requisition", Guid.NewGuid(), seq: 1),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), CancellationToken.None);

        Assert.Empty(replicaA.LocalPresence());
        Assert.Empty(replicaB.LocalPresence());
    }

    /// <summary>
    /// Regressão da rodada 3 da revisão: enter e leave são publishes independentes, sem garantia de
    /// ordem entre eles quando cruzam réplicas — a rede pode entregar o leave (seq maior, porque o
    /// cliente o emitiu DEPOIS) antes do enter (seq menor). Sem o desempate por seq, o enter "vencia"
    /// por chegar por último e a presença ficava presa até a conexão cair. O seq da aba (a única fonte
    /// de ordem real, já que réplicas não compartilham relógio) resolve: um comando com seq ≤ o último
    /// aplicado é descartado, então o enter atrasado nunca é aplicado.
    /// </summary>
    [Fact]
    public async Task Leave_com_seq_maior_aplicado_antes_do_enter_com_seq_menor_nao_deixa_presenca()
    {
        var (backplane, replicaA, _) = await TwoReplicasAsync();
        var userId = Guid.NewGuid();
        var resource = Guid.NewGuid();
        using var sub = replicaA.Subscribe(userId, "Requester", isAdmin: false);

        // O leave (seq=2, emitido DEPOIS pelo cliente) chega e é aplicado ANTES do enter (seq=1) —
        // exatamente a ordem que a rede pode entregar entre dois publishes independentes.
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, userId, "requisition", resource, seq: 2),
            new RealtimeSignal(PresenceCommandKind.Leave, null)), CancellationToken.None);
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(sub.ConnectionId, userId, "requisition", resource, seq: 1),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), CancellationToken.None);

        Assert.Empty(replicaA.LocalPresence());
    }
}
