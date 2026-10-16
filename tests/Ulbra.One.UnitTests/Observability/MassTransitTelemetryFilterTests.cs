using Ulbra.One.Common.Infrastructure.Observability;

namespace Ulbra.One.UnitTests.Observability;

// The SQL below is copied verbatim from what MassTransit emits against Postgres, including the
// two sweepers quoting the schema differently — the inbox one does not, the outbox one does.
public class MassTransitTelemetryFilterTests
{
    private const string InboxCleanupSql = """
        SELECT i.id, i.consumed, i.consumer_id, i.delivered, i.expiration_time, i.last_sequence_number, i.lock_id, i.message_id, i.receive_count, i.received, i.row_version
        FROM masstransit.inbox_state AS i
        WHERE i.delivered IS NOT NULL AND i.delivered < @removeTimestamp
        ORDER BY i.delivered
        LIMIT @p
        """;

    private const string OutboxDeliverySql = """
        SELECT m.outbox_id, m.created, m.delivered, m.last_sequence_number, m.lock_id, m.row_version
        FROM (
            SELECT * FROM "masstransit"."outbox_state" ORDER BY "created" LIMIT ? FOR UPDATE SKIP LOCKED
        ) AS m
        LIMIT ?
        """;

    private const string OutboxMessageSql = """
        SELECT m.sequence_number, m.body, m.message_id, m.message_type
        FROM masstransit.outbox_message AS m
        WHERE m.outbox_id = @outboxId
        ORDER BY m.sequence_number
        """;

    [Fact]
    public void Drops_the_inbox_cleanup_sweep() =>
        Assert.True(MassTransitTelemetryFilter.IsOutboxPolling(InboxCleanupSql));

    [Fact]
    public void Drops_the_outbox_delivery_sweep() =>
        Assert.True(MassTransitTelemetryFilter.IsOutboxPolling(OutboxDeliverySql));

    [Fact]
    public void Keeps_the_actual_message_delivery() =>
        Assert.False(MassTransitTelemetryFilter.IsOutboxPolling(OutboxMessageSql));

    [Theory]
    [InlineData("SELECT p.id, p.amount FROM finance.payables AS p WHERE p.company_id = @cid")]
    [InlineData("INSERT INTO audit.audit_log (id, action) VALUES (@p0, @p1);")]
    [InlineData("SELECT count(*)::int FROM notifications.notifications AS n WHERE n.read_at IS NULL")]
    public void Keeps_application_queries(string sql) =>
        Assert.False(MassTransitTelemetryFilter.IsOutboxPolling(sql));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Keeps_anything_it_cannot_read(string? sql) =>
        Assert.False(MassTransitTelemetryFilter.IsOutboxPolling(sql));

    [Fact]
    public void Ignores_casing() =>
        Assert.True(MassTransitTelemetryFilter.IsOutboxPolling(
            "SELECT * FROM MASSTRANSIT.OUTBOX_STATE ORDER BY created"));

    [Fact]
    public void Does_not_match_on_the_schema_name_alone() =>
        Assert.False(MassTransitTelemetryFilter.IsOutboxPolling(
            "SELECT m.body FROM masstransit.outbox_message AS m JOIN finance.payables AS p ON true"));
}
