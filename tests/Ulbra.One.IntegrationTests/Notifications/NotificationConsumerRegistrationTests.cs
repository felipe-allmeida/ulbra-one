using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Api;
using Xunit;

namespace Ulbra.One.IntegrationTests.Notifications;

/// <summary>Um hop adiante de <see cref="NotificationConsumerOutboxEnrollmentTests"/>, e a mesma técnica: aquele
/// prova que um consumer que publica está enrolado no outbox; este prova que os consumers do módulo estão de
/// fato REGISTRADOS na configuração do MassTransit. Apagar o <c>x.AddConsumer&lt;NotificationCreatedConsumer&gt;()</c>
/// de MessagingSetup compila limpo (nome totalmente qualificado, sem using órfão) e deixava a suíte inteira
/// verde — enquanto a cadeia notificação → browser morria em produção: sem esse consumer, ninguém consome
/// NotificationCreated e nada é publicado no backplane. Reflexão sobre o conjunto de registro (não sobre uma
/// lista escrita à mão) para que um consumer novo no módulo entre nesta guarda sozinho. Sem banco: só inspeciona
/// o IServiceCollection que AddMessaging monta.</summary>
public class NotificationConsumerRegistrationTests
{
    [Fact]
    public void Todos_os_consumers_de_Notifications_estao_registrados_no_MassTransit()
    {
        var services = new ServiceCollection()
            .AddMessaging(new ConfigurationBuilder().Build());
        var registered = services.Select(d => d.ServiceType).ToHashSet();

        var consumers = typeof(Ulbra.One.Notifications.Infrastructure.Messaging.NotificationCreatedConsumer)
            .Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
            .ToList();

        // Os cinco que materializam notificação mais o NotificationCreatedConsumer, que fecha a cadeia até o
        // browser. Se este número mudar, mude-o deliberadamente — não o ajuste só para o teste passar.
        Assert.Equal(6, consumers.Count);

        foreach (var consumer in consumers)
            Assert.True(registered.Contains(consumer),
                $"{consumer.Name} implementa IConsumer<> mas não está registrado em MessagingSetup.AddMessaging — "
                + "sem x.AddConsumer<> nenhuma fila é criada para ele e as mensagens que ele consumiria somem.");
    }
}
