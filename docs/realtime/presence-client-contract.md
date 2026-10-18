# Web client — presence contract

Status: **authoritative**. Where the code in `web/` disagrees with this document, the document wins.

A screen that shows presence (who is looking at a requisition) calls `usePresence(scope, resourceId)`. While the
screen is open, the user must be listed as present; after the user leaves it, they must not be. A browser tab has
one SSE connection (`connectionId`) for its whole life; the user can open and close many screens in that tab
without reloading.

## Server behaviour (given, not changing)

`POST /api/realtime/presence` (body `{connectionId, scope, resourceId, seq}`) enters and
`DELETE /api/realtime/presence?connectionId=&scope=&resourceId=&seq=` leaves. For each
`(connectionId, scope, resourceId)` the server applies a command **only if its `seq` is greater than the last
`seq` it applied for that triple**; a command with a smaller or equal `seq` is discarded silently (still `204`).
While the tab's SSE connection stays open, the server keeps a user who entered and did not leave present
indefinitely (heartbeat renewal); the 45 s TTL only applies once the connection is gone.

## Guarantees

**PC1. Open enters, close leaves.** Opening a presence screen makes the user present; closing it (navigating
away, unmounting) makes them absent. Screens for different resources in the same tab are independent.

**PC2. Order.** A tab's presence commands must reach the server in a form the rule above applies in the order the
user acted.

**PC3. Re-opening works.** Re-opening the same resource in the same tab — any number of times, without reloading
— makes the user present again.

**PC4. A lost leave still takes effect.** If the leave fails with a network error or a 5xx (e.g. it reached a
replica that is shutting down), it must still take effect while the tab stays open: it is retried and takes
effect **within 10 s**. A 4xx response is final and is not retried. Retrying is bounded: **at most 3 attempts in
total** per leave.

**PC5. A retry never undoes a later re-open.** A retried leave must never make the user absent after they
re-opened the same resource in the same tab.

**PC6. Unchanged.** No periodic presence requests (renewal is the server's job); the HTTP requests keep exactly
the shape above.
