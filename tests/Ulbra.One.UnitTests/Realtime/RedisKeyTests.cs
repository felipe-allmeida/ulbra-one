using Ulbra.One.Common.Infrastructure.Realtime;

namespace Ulbra.One.UnitTests.Realtime;

public class RedisKeyTests
{
    [Fact]
    public void Chave_de_presenca_inclui_escopo_e_recurso()
    {
        var resource = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            "realtime:presence:requisition:11111111-1111-1111-1111-111111111111",
            RealtimeRedisKeys.Presence("requisition", resource));
    }

    [Fact]
    public void Canal_e_unico_e_estavel()
    {
        Assert.Equal("realtime:signals", RealtimeRedisKeys.Channel);
    }
}
