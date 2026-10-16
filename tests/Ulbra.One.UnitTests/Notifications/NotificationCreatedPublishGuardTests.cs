using MassTransit;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Application.Messaging;
using Ulbra.One.Notifications.Domain.Entities;
using Ulbra.One.Notifications.Infrastructure.Messaging;
using Ulbra.One.UnitTests.Finance; // TestAsyncEnumerable — the async-queryable shim NotificationWriter's AnyAsync needs
using Xunit;

namespace Ulbra.One.UnitTests.Notifications;

/// <summary>Pins the "publish only on an actual insert" guard (the <c>if (created is not null)</c> check) that
/// every transition consumer wraps its <c>publish.Publish</c> call in. That guard is the whole contract the
/// realtime chain rests on: without it, a redelivery — which NotificationWriter correctly no-ops — would still
/// re-publish NotificationCreated, and a transition the mapper skips would publish a notification that was
/// never persisted. Deleting the guard from any of the five consumers must fail one of these tests.</summary>
public class NotificationCreatedPublishGuardTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequisitionTransitionedConsumer_publica_uma_unica_vez_mesmo_com_redelivery()
    {
        var uow = new FakeNotificationUow();
        var publish = new RecordingPublish();
        var consumer = new RequisitionTransitionedConsumer(uow, publish);
        var message = new RequisitionTransitioned(
            Guid.NewGuid(), Guid.NewGuid(), Company, 42, "Draft", "Triage", "Submit", Actor, Requester, null, At);

        await consumer.ProcessAsync(message, CancellationToken.None);
        await consumer.ProcessAsync(message, CancellationToken.None); // at-least-once redelivery, same TransitionId

        Assert.Single(publish.Messages.OfType<NotificationCreated>());
    }

    [Fact]
    public async Task RequisitionTransitionedConsumer_nao_publica_quando_mapper_nao_gera_notificacao()
    {
        var uow = new FakeNotificationUow();
        var publish = new RecordingPublish();
        var consumer = new RequisitionTransitionedConsumer(uow, publish);
        // FromStatus == ToStatus is the mapper's explicit null case (audit-only trail entry, e.g. AddRecurrenceCycle).
        var message = new RequisitionTransitioned(
            Guid.NewGuid(), Guid.NewGuid(), Company, 42, "InPayment", "InPayment", "AddRecurrenceCycle", Actor, Requester, null, At);

        await consumer.ProcessAsync(message, CancellationToken.None);

        Assert.Empty(publish.Messages);
    }

    [Fact]
    public async Task BudgetAdjustmentTransitionedConsumer_publica_uma_unica_vez_mesmo_com_redelivery()
    {
        var uow = new FakeNotificationUow();
        var publish = new RecordingPublish();
        var consumer = new BudgetAdjustmentTransitionedConsumer(uow, publish);
        var message = new BudgetAdjustmentTransitioned(
            Guid.NewGuid(), Guid.NewGuid(), Company, 7, "AwaitingTriage", "AwaitingApproval", "SendToApproval", Actor, Requester, null, At);

        await consumer.ProcessAsync(message, CancellationToken.None);
        await consumer.ProcessAsync(message, CancellationToken.None); // at-least-once redelivery, same TransitionId

        Assert.Single(publish.Messages.OfType<NotificationCreated>());
    }

    [Fact]
    public async Task BudgetAdjustmentTransitionedConsumer_nao_publica_quando_mapper_nao_gera_notificacao()
    {
        var uow = new FakeNotificationUow();
        var publish = new RecordingPublish();
        var consumer = new BudgetAdjustmentTransitionedConsumer(uow, publish);
        // ToStatus not in the switch (e.g. "Draft") → the mapper's default null arm.
        var message = new BudgetAdjustmentTransitioned(
            Guid.NewGuid(), Guid.NewGuid(), Company, 7, "AwaitingTriage", "Draft", "Reset", Actor, Requester, null, At);

        await consumer.ProcessAsync(message, CancellationToken.None);

        Assert.Empty(publish.Messages);
    }

    /// <summary>In-memory IUnitOfWork serving Notification rows through an async-compatible queryable
    /// (NotificationWriter's dedup check calls EF's <c>AnyAsync</c>, which needs IAsyncQueryProvider).
    /// Persists across ProcessAsync calls on the same instance, so a redelivery sees the first insert.</summary>
    private sealed class FakeNotificationUow : IUnitOfWork
    {
        private readonly List<Notification> _notifications = [];

        public IQueryable<T> ReadSet<T>() where T : class =>
            new TestAsyncEnumerable<T>(_notifications.OfType<T>());

        public IQueryable<T> Set<T>() where T : class => ReadSet<T>();

        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class
        {
            if (entity is Notification n) _notifications.Add(n);
            return Task.CompletedTask;
        }

        public void Remove<T>(T entity) where T : class { }
        public void RemoveRange<T>(IEnumerable<T> entities) where T : class { }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public void ClearChangeTracker() { }
    }

    /// <summary>Records every published message so tests can assert type + count.</summary>
    private sealed class RecordingPublish : IPublishEndpoint
    {
        public List<object> Messages { get; } = [];
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
    }
}
