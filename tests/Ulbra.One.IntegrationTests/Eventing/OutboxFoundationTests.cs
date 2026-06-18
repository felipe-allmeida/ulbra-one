using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests.Eventing;

/// <summary>
/// Proves that MassTransit EF bus outbox is wired correctly:
/// a publish inside a scope with AppDbContext is captured atomically in
/// masstransit.outbox_message on the same SaveChangesAsync call.
/// </summary>
[Collection("Postgres")]
public class OutboxFoundationTests(PostgresFixture pg)
{
    /// <summary>Minimal test message — scoped to this file, no shared Contracts project needed.</summary>
    private sealed record FoundationPing(Guid Id, string Label);

    [Fact]
    public async Task Publish_within_scope_is_captured_in_outbox_message_table()
    {
        await using var factory = new ApiFactory(pg.ConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var msg = new FoundationPing(Guid.NewGuid(), "outbox-foundation");
        await publisher.Publish(msg);
        await db.SaveChangesAsync();

        // Query the outbox_message table directly — this is the EF-backed outbox table.
        // SqlQueryRaw<int> maps the single column by the name "Value".
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM masstransit.outbox_message")
            .SingleAsync();

        Assert.True(count > 0,
            "Expected at least one row in masstransit.outbox_message after Publish + SaveChangesAsync, " +
            "but the table was empty. The EF bus outbox may not be capturing publishes correctly.");
    }
}
