# Presence contract (realtime)

Status: **authoritative**. Where the code disagrees with this document, the document wins.

Presence answers "who is looking at this resource right now" (`GET /api/realtime/presence`). It is
shown as avatars on the requisition screen. This document states what the API guarantees. It does not
prescribe an implementation.

## Definitions

- **Tab**: one open SSE stream (`GET /api/realtime/stream`). The stream's first event, `connected`,
  carries the tab's `connectionId`.
- A tab **enters** a resource with `POST /api/realtime/presence` (`{connectionId, scope, resourceId}`)
  and **leaves** it with `DELETE /api/realtime/presence?connectionId=&scope=&resourceId=`. A tab makes
  these calls one at a time: it waits for each response before sending the next presence call.
- A tab is **in** a resource after a successful enter for it, until its next successful leave for it,
  or until the tab's stream ends (tab closed, network lost, or the replica holding the stream shut down).
- A user is **present** in a resource while at least one of the user's own tabs is in it.
- **Replica**: one API process. Production runs two or more replicas behind a load balancer. There is
  no session affinity: a tab's stream and each of its presence calls can be served by different replicas.
- Replicas share one Redis (pub/sub + presence state). Redis can be slow, unreachable, or restarted
  without persistence (all keys lost).

Timing constants (`RealtimeDefaults`): heartbeat `H = 20 s`, presence TTL `T = 45 s`.

## Guarantees

**P1. Fast, consistent reads.** Under normal conditions (Redis reachable, no replica lagging),
`GET /presence` on every replica reflects an enter or a leave within **2 s** of the call returning.

**P2. Eventual correctness, whatever the delivery order.** Messages between replicas can be delivered
late (seconds) and, across different replicas, in any order; one replica's path to Redis can lag
behind another's. Whatever the order or delay, `GET /presence` on every replica is correct within
**T + H + 5 s (70 s)** of the last enter/leave/close, and stays correct afterwards:

- a user who has left (or whose tabs are all closed) is not listed, and does not reappear;
- a user who is present is listed, even if they left and re-entered the same resource in quick succession.

**P3. Self-healing.** If Redis loses presence state (restart without persistence, eviction, a lost
write, an outage longer than the TTL), every user who is still present (P-definition above) is listed
again within **H + 5 s (25 s)** of Redis being reachable, with no action from the client. Enters and
leaves made after the loss keep working as in P1.

**P4. Graceful drain.** When a replica shuts down gracefully (rolling deploy), tabs whose streams it
held are no longer in anything. Within **3 s** of the shutdown completing, users whose tabs were all on
that replica are not listed on the other replicas, and tabs on other replicas that are in an affected
resource receive a `presence` signal for it (so the screen refetches).

**P5. Degradation.** Realtime is accessory. With Redis unreachable, `POST`, `DELETE` and
`GET /presence` and the stream still answer within **3 s** (`204`, `204`, `200`, `200`); presence may be
empty or stale meanwhile.

**P6. Authorization (unchanged).** Unknown scopes are refused; a caller who cannot view the resource
cannot enter it (404) and reads an empty list; a `connectionId` owned by another user never changes that
user's presence.

**P7. Compatibility.** The web client and the other consumers of these endpoints are **not** changing
in this release: they send exactly today's requests (the body and query parameters listed above, nothing
else). Every guarantee must hold for those requests. You may add optional fields, but nothing may
depend on clients sending them.

## Known limitation (accepted, out of scope)

A user with several tabs in the same resource may disappear for up to `H` when one of those tabs
leaves, until the next renewal lists them again. P1 is not required to hold for that case.
