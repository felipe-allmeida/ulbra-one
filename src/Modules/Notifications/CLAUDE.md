# Notifications Module

**Purpose:** In-app notification inbox. Listens to domain events published by other modules and persists
role-targeted notifications that the frontend inbox can poll.
**Schema:** `notifications`  ·  **UoW:** inject `IUnitOfWork` directly (no key)

## Boundaries

- Belongs here: the `Notification` entity and its lifecycle; `GET /api/notifications` inbox endpoint;
  `IConsumer<T>` implementations that translate domain contracts into `Notification` rows.
- Does NOT belong: the originating domain logic (Finance owns payable state), user management (Identity),
  email/push delivery (future slices), read/dismiss state (deferred to v2).
- Cross-module refs: `ReferenceId` is a loose `Guid` — no FK constraint to Finance or any other schema.
- Consumer registration is done in `MessagingSetup` (API composition root), not inside this module.
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application;
  Infrastructure → Application + Domain.

## How it consumes events

1. A domain event contract (e.g. `PayablePendingApproval`) is published via `IPublishEndpoint` in a
   Finance handler before `SaveChanges`; the EF bus outbox commits it atomically.
2. MassTransit delivers the message to `PayablePendingApprovalConsumer` (at-least-once).
3. The consumer checks for an existing `Notification` with matching `(ReferenceId, Type)` — if found,
   it returns early (idempotency). Otherwise it creates and saves a new `Notification`.

## Notification model

`Notification` fields: `Id` (Guid), `CompanyId`, `AudienceRole` (e.g. `"Admin"`), `Type`
(e.g. `"PayablePendingApproval"`), `Title`, `Message`, `ReferenceId` (nullable Guid), `OccurredAt`.
**No read/dismiss state in v1.** The inbox returns all matching notifications newest-first.

Inbox filtering: `GET /api/notifications` filters by `AudienceRole == UserContext.Role` — callers see
only their own role's notifications.

## Key files

- `Ulbra.One.Notifications.Domain/Entities/Notification.cs` — entity
- `Ulbra.One.Notifications.Application/Consumers/PayablePendingApprovalConsumer.cs` — event handler
- `Ulbra.One.Notifications.Application/Endpoints/ListNotifications/ListNotifications.cs` — inbox endpoint
- `Ulbra.One.Notifications.Application/Endpoints/ListNotifications/NotificationResponse.cs` — response DTO
- `Ulbra.One.Notifications.Infrastructure/NotificationConfiguration.cs` — EF config (schema, indexes)
- `Ulbra.One.Notifications.Infrastructure/NotificationsModule.cs` — `AddNotificationsModule` (no-op in v1)
- `src/API/Ulbra.One.Api/MessagingSetup.cs` — consumer + outbox registration (composition root)

## Testing

- Integration tests cover the full outbox → broker → consumer → `Notification` path using
  Testcontainers Postgres + RabbitMQ (`tests/Ulbra.One.IntegrationTests`).
- Key scenarios: end-to-end delivery, atomicity (publish rolls back if business tx fails), idempotency
  (duplicate message does not create a duplicate notification).
- Run: `dotnet test tests/Ulbra.One.IntegrationTests`

## Future slices (deferred)

- Read/dismiss state per user.
- Email and push delivery channels.
- Hangfire time-based escalation for stale approvals.
- MassTransit consumer inbox middleware (v1 uses app-level idempotency guard).
