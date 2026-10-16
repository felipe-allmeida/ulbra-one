# Web client — realtime freshness contract

Status: **authoritative**. Where the code in `web/` disagrees with this document, the document wins.

The web client keeps server state in TanStack Query (`web/src/queryClient.ts` defaults: `staleTime` 30 s, no
refetch on window focus). A server-sent-events stream (`openRealtimeStream`, `RealtimeProvider`) tells the
client *that* something changed; the client then refetches. This document states what the user must see.

## Guarantees

**F1. Signals are never lost.** When a realtime signal arrives, every mounted query it covers must end up showing
data fetched *after* the signal (within 2 s of the server answering). This includes a query that was still on
its **first load** when the signal arrived. Coverage:
- `notification` → the notification list and the unread count (bell);
- `presence` with a resource id → the presence list of that resource.

**F2. Same for reconnections and for the user's own writes.** A (re)connection of the stream refreshes everything
the stream covers (notifications, unread count, presence), and the user's own writes refresh what they change
(marking one or all notifications as read → list and unread count). The F1 rules apply, including during a first
load.

**F3. No amplification, no new polling.** A burst of signals arriving while a load is in flight causes at most
one additional request per affected query. No new periodic requests are introduced (the existing safety-net
interval of the unread count stays exactly as it is).

**F4. Reconnect backoff.** When a connection fails or ends, the client waits before reconnecting: 1 s, then
doubling, up to 30 s. A connection that ends within **2 s** of opening counts as failed even if it delivered data
— the server writes the `connected` event and a ping immediately on open, even from a replica that is shutting
down. After a connection that stayed open longer than 2 s and kept receiving data, the next wait is 1 s again.

**F5. Unchanged.** 401 logs the user out and stops; 403 stops without logging out; the token goes in the
`Authorization` header.
