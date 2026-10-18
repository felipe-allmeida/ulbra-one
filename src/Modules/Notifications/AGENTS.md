# Notifications Module

**Purpose:** In-app notification inbox. Consumes the esteira transition events published by Budgeting and
persists notifications addressed to a role or a user, which the frontend inbox lists and the bell badge polls.
**Schema:** `notifications`  ·  **UoW:** inject `IUnitOfWork` directly (no key)

## Boundaries

- Belongs here: the `Notification` entity and its lifecycle; the `/api/notifications` endpoints
  (list / unread-count / mark-read / read-all); `IConsumer<T>` implementations that translate transition
  events into `Notification` rows; the pure `NotificationMapper`.
- Does NOT belong: the originating domain logic (Budgeting owns the esteira and publishes the events),
  user management (Identity), email/push delivery (future — a sibling consumer of the same events).
- Cross-module refs: `ReferenceId` is a loose `Guid` into the requisition/adequação — no FK. The event
  contracts live in `Ulbra.One.Budgeting.Application.Messaging` (Infrastructure references it, the same way
  `Finance.Infrastructure` does for `RequisitionApprovedForPayment`).
- Consumer registration is in `MessagingSetup` (API composition root), not inside this module.
- Dependency rule: Domain → Common.Domain only; Application → Domain + Common.Application (no Budgeting dep —
  the endpoints don't need it); Infrastructure → Application + Domain + Common.* + Budgeting.Application.

## Audience model (audience-by-role OR by-user)

A `Notification` targets EITHER a role (`AudienceRole`, e.g. the whole Accounting team) OR a single user
(`AudienceUserId`, the solicitante) — exactly one, enforced by the `ForRole` / `ForUser` factories.
The inbox filter is `AudienceUserId == me OR AudienceRole == myRole OR (I'm Admin AND AudienceRole != null)`
— Admin sees every role's notifications (it has no Accounting/Controllership role of its own).
Read state (`ReadAt`) is **shared**: a role notification cleared by one team member clears for the team
(it's a work-queue signal; the actual work lives in the requisition/adequação lists).

The transition → audience map (the two esteiras have different audiences post-AEL-842):

| Transition (ToStatus)              | Audience                | Type                             |
|------------------------------------|-------------------------|----------------------------------|
| Requisição → Triage                | role Controllership     | RequisitionAwaitingTriage        |
| Requisição → FinancialComplement   | user (solicitante)      | RequisitionAwaitingFinancialComplement |
| Requisição → Approval              | role Approver    | RequisitionAwaitingApproval      |
| Requisição → Complement            | user (solicitante)      | RequisitionReturnedForComplement |
| Requisição → InPayment             | user (solicitante)      | RequisitionApproved              |
| Requisição → Cancelled             | user (solicitante)      | RequisitionCancelled             |
| Adequação created (AwaitingTriage) | role Accounting         | BudgetAdjustmentAwaitingTriage          |
| Adequação → AwaitingApproval       | role Controllership     | BudgetAdjustmentAwaitingApproval        |
| Adequação → Approved/Cancelled/Reversed | user (solicitante) | BudgetAdjustment{Approved,Cancelled,Reversed} |

Skips: a ToStatus not in the map produces nothing; personal notifications are skipped when there is no
requester (`CreatedBy` was "system") or the actor IS the requester (self-action, e.g. the requester returning
or cancelling their own requisition). The type decides the frontend deep-link: `Requisition*` →
`/app/requisitions/:id`, `BudgetAdjustment*` → `/app/adequacoes/:id`.

## How it consumes events

1. Budgeting publishes `RequisitionTransitioned` / `BudgetAdjustmentTransitioned` via `IPublishEndpoint` before
   `SaveChanges` on each transition endpoint (EF bus outbox → atomic with the business write).
2. MassTransit delivers to `RequisitionTransitionedConsumer` / `BudgetAdjustmentTransitionedConsumer` (at-least-once).
3. `NotificationMapper.Map(event)` returns the notification to create (or null). The consumer checks for an
   existing row with the same `SourceTransitionId` (idempotency), then inserts. The unique index
   `(SourceTransitionId, Type)` + a narrow catch of Postgres 23505 close the concurrent-redelivery race.
   A legitimately repeated transition (e.g. return-for-complement twice) has distinct transition ids, so it
   correctly yields two notifications.
4. When (and only when) `NotificationWriter.PersistAsync` actually inserted the row, the consumer publishes
   `NotificationCreated(NotificationId, AudienceUserId, AudienceRole)` — thin addressing only, no content.
   `NotificationCreatedConsumer` (the same shared queue, so one replica processes it) receives it and calls
   `IRealtimeBackplane.PublishAsync`, which fans out to every replica; each replica pushes the signal down its
   own SSE connections for the matching user/role. The frontend then refetches the inbox, which re-applies
   authorization — the realtime signal never carries notification content. See `docs/backend-conventions.md`
   §12 for the outbox mechanics and `Common.Application/Realtime` (Task 1 of the realtime-updates design) for
   the backplane contracts.

## Key files

- `Domain/Entities/Notification.cs` — entity (role XOR user audience; `ReadAt`; factories; `MarkRead`)
- `Application/Endpoints/NotificationAudience.cs` — the shared inbox predicate
- `Application/Endpoints/*` — list / unread-count / mark-read / read-all + `NotificationResponse`
- `Application/Messaging/NotificationCreated.cs` — the realtime event, published only on a real insert
- `Infrastructure/Messaging/NotificationMapper.cs` — pure transition → notification mapping
- `Infrastructure/Messaging/*Consumer.cs` + `NotificationWriter.cs` — idempotent persistence; the five transition/
  request/attached/breach consumers each publish `NotificationCreated` after `PersistAsync` returns non-null
- `Infrastructure/Messaging/NotificationCreatedConsumer.cs` — PUBLISHes the realtime signal on the backplane;
  does not touch the database
- `Infrastructure/NotificationConfiguration.cs` — EF config (schema, indexes, unique guard)
- `src/API/Ulbra.One.Api/MessagingSetup.cs` — consumer registration + `OutboxEnrolledEndpoints` (composition root)

## Atomic on both sides of the broker

Publishing rides the bus outbox on the Budgeting side, so the transition event is atomic with the transition
itself. On the consuming side, the five transition/request/attached/breach consumers (the ones that call
`NotificationWriter.PersistAsync` and then publish `NotificationCreated`) run under the **consumer inbox/outbox**
(`UseEntityFrameworkOutbox` on their endpoint, gated by `MessagingSetup.OutboxEnrolledEndpoints`): the inbox
short-circuits a redelivery before the consumer body even runs, and inside the outbox transaction the
`Notification` row and the staged `NotificationCreated` outbox row commit together — there is no window where
one persists without the other. `NotificationCreatedConsumer` (which only calls the realtime backplane, no
database write) is not in that set — it doesn't need to be, since it has nothing to make atomic with a save.
A consumer that keeps failing still sends the message to the error queue (notification lost silently) —
monitor the DLQ. Adding a **sixth** publishing consumer without enrolling its endpoint in
`OutboxEnrolledEndpoints` reintroduces silent loss (no exception, no log — the staged outbox row is discarded on
scope dispose); `NotificationConsumerOutboxEnrollmentTests` (IntegrationTests) fails the build if that happens.

## Future slices (deferred)

- Email / push delivery (a sibling consumer of the same events; needs an email in `IUserDirectory`).
- Per-user read state (upgrade without changing the endpoint contracts).
- Notifications for payment-driven transitions (MarkPaid/Reopen live in a consumer, not the endpoints).
- Multi-company scoping of the role inbox (the JWT carries no CompanyId today).
