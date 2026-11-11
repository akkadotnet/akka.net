# Reminder Processing: Failure Modes and Design Decisions

This document is the behavioral specification for the reminder scheduler and its behavior model.
Model assertions must follow the guarantees stated here; a regression test does not establish a new
guarantee by itself.

## Delivery Semantics

Akka.Reminders uses **at-least-once delivery with explicit acknowledgement**.

- Reminders are delivered through `IShardRegionResolver.DeliverReminder`, which wraps the message in a `ShardingEnvelope(entityId, ReminderEnvelope<T>)` and sends it to the shard region via fire-and-forget `Tell`. The consumer actor receives a strongly-typed `ReminderEnvelope<T>`.
- Consumers MUST be idempotent.
- Each occurrence is identified by `(ReminderEntity, ReminderKey, DueTimeUtc)`.
- The envelope exposes a non-null `Deadline` value object. Unbounded reminders use `ReminderDeadline.Infinite`.
- Retries for each occurrence are bounded by `MaxDeliveryAttempts` and the occurrence deadline.

### Latest-only recurring reminders

Recurring reminders are modeled as a stream of occurrences.

- The next occurrence is persisted, in the same commit, the first time the current occurrence is processed: when it is delivered, when it is put back for a retry because its shard region is missing, or when it ends without delivery (expired or failed).
- The next occurrence is the earliest slot whose deadline has not passed. Missed slots are skipped, not replayed.
- Each occurrence starts with a new retry budget.
- Each occurrence has its own absolute UTC deadline.
- By default, a recurring occurrence expires when the next occurrence is delivered or becomes due, whichever is first. There is at most one live occurrence per recurring reminder: when the next one is sent early (inside `MaxSlippage`) while the previous one is still unacked, the previous one is marked `Expired` in the same commit.
- A first attempt writes its successor with the existing overwrite semantics. Explicit re-registration replaces active work and can reopen matching occurrence identities, including a cancelled successor left by the prior registration.
- A retry inserts its successor only if absent inside the same atomic mutation commit. Existing Pending, AwaitingAck and terminal occurrences keep their state and retry budget; retries never reset a successor. No per-occurrence status lookup is required.
- Superseding an older occurrence is an active-only completion in that commit: absent or terminal rows are unchanged.
- If `MaxDeliveryWindow` is configured, the effective deadline is `min(due + window, next due)`.
- A late ack for an old occurrence is a harmless `NotFound` because the ack is matched by `DueTimeUtc`.

## Scheduler Initialization

On startup, the scheduler actor is in an initial behavior that stashes all client messages until initialization completes.

1. `PreStart` calls `LoadReminderOverview`, which fetches the `ReminderOverview` (an efficient `COUNT`/`MIN` aggregate — not a full table scan) and the next awaiting-ack deadline from storage in a single async pipeline, bundled into an `InitResult` record.
2. The result is delivered to `Self` via `PipeTo`.
3. On receipt, the scheduler schedules the ack-timeout check synchronously, transitions to the `Scheduling` behavior, then calls `UnstashAll` to replay buffered client messages.

This ensures the scheduler is fully initialized — with ack-timeout tracking active — before any client messages are processed. There is no intermediate `RunTask` between the behavior transition and client message replay.

### Recovery of AwaitingAck state after restart

There is no explicit recovery step that resets `AwaitingAck` rows back to `Pending` on startup. Instead, `InitResult.NextAckDeadline` schedules the first ack-timeout check, which naturally discovers any `AwaitingAck` rows whose deadline has elapsed and retries or expires them through the normal `CheckAckTimeouts` path.

### Reply ordering

Schedule and cancel handlers reply to the caller **after** `ReloadPendingOverviewAsync` and `TryScheduleFetchReminders` complete. This guarantees the Ask response is a reliable signal that the fetch timer is registered — callers can depend on the scheduler being ready to process the reminder on the next tick.

If the reminder was stored but the overview reload then fails, the schedule handler still replies `Success`: it arms the fetch timer from the due time of the reminder it just stored, and the next fetch reads a fresh overview. This covers schedule only.

Known limit: a cancel or cancel-all whose overview reload fails still replies `Error`, although the cancel was stored.

### Meaning of a scheduling error

A scheduling `Error` or caller-side timeout means that acceptance is unknown.
It does not prove that the write was rolled back or that storage was unchanged. A stored reminder may
therefore be delivered even though the caller received `Error`.

If the scheduling write persisted but reported failure, the scheduler must automatically rediscover
the persisted work once storage is available again. Recovery must not require another client command
or a manual restart. The scheduler processes eligible occurrences under the normal deadline,
retry-budget, and latest-only rules; expired, exhausted, cancelled, or superseded work does not gain
a new delivery guarantee. If the write did not persist, recovery must not create the reminder.

Repeating a schedule call is a new scheduling operation, not an idempotent replay of acceptance. It can
replace active work for the same entity and key and reset occurrence state. Consumers must remain
idempotent if the caller schedules again after an uncertain result.

## Processing Pipeline

### Scheduler tick

Each tick is triggered by a `FetchReminders` timer. The timer delay is derived from the pending overview's `TimeUntilNext` value, plus the `MaxSlippage` setting (which causes the scheduler to fetch reminders slightly ahead of their due time to avoid re-scheduling overhead).

`TimeUntilNext` counts from the clock reading the overview was computed against. The scheduler keeps that reading and, when it arms the timer, subtracts the time that has passed since, so slow storage calls in between do not make the next fetch late.

```text
Flush buffered ack writes (if any)
  -> Expire stale occurrences (best effort)
  -> Fetch due Pending reminders (bounded batch, up to MaxBatchSize)
  -> Process in DeliveryCommitChunkSize chunks:
      -> Classify each occurrence:
          - Deadline expired -> terminal (Expired)
          - Shard region not found -> retry or terminal (Failed/Expired)
          - Deliverable -> create AwaitingAck state
      -> For recurring reminders: pre-create next occurrence
      -> Commit all mutations in a single CommitReminderMutationsAsync call:
          - Pending upserts and insert-only retry successors
          - Terminal completions and active-only supersession
          - AwaitingAck transitions
      -> Deliver ReminderEnvelope<T> only after the commit succeeds
  -> Update overview (incrementally; bounded fetch reconciles conditional mutations, reload on failure)
  -> Schedule next fetch timer
```

### Ack handler

Acks are **not** written to storage immediately. They are buffered in memory and flushed in batches.

```text
ReminderAck received:
  -> Buffer in _bufferedAcknowledgements, keyed by (Entity, Key, DueTimeUtc)
  -> Duplicate acks for the same occurrence are coalesced (additional senders appended)
  -> Schedule a FlushBufferedAcks self-message (debounced by flag)

FlushBufferedAcks:
  -> Drain buffer in batches of AckFlushBatchSize (default 256)
  -> Call Storage.AcknowledgeRemindersAsync per batch
  -> Storage checks: still AwaitingAck? Before deadline? -> mark Delivered
  -> Stale, superseded, expired, or already-acked -> return NotFound
  -> Reply to all buffered senders with the result
  -> On success, refresh the ack-timeout schedule from storage
  -> On failure, reply Error to senders; an unapplied write leaves AwaitingAck
    for timeout recovery, while an accepted acknowledgement remains Delivered
```

Buffered acks are also flushed at the start of each `FetchReminders` tick and each `CheckAckTimeouts` tick, ensuring pending acks are committed before new work begins.

### Ack-timeout checker

Ack-timeout checking is **event-driven**, not periodic. After each delivery commit (whether it reports success or failure), the scheduler computes the earliest ack deadline in the batch and schedules a one-shot `CheckAckTimeouts` timer at exactly that deadline. The timer is replaced if an earlier deadline is found.

```text
CheckAckTimeouts fires:
  -> Flush buffered ack writes (if any)
  -> Expire stale occurrences
  -> Scan storage for AwaitingAck rows whose ack deadline has elapsed
  -> For each timed-out occurrence:
      -> If retry is possible (within MaxDeliveryAttempts and deadline):
          schedule retry with exponential backoff
      -> Otherwise: mark Failed or Expired
  -> Commit mutations via CommitReminderMutationsAsync
  -> Refresh ack-timeout schedule from storage
  -> If processing failed or changed rows, reload pending overview and arm the fetch timer
```

### Negative acknowledgement handler

Consumers call `IReminderClient.NackAsync` when an attempt fails before `AckTimeout`.
The scheduler flushes older buffered acknowledgements before it handles the negative acknowledgement.
It then verifies that the exact occurrence still has `AwaitingAck` status.

```text
ReminderNack received:
  -> Flush buffered ack writes
  -> Find the AwaitingAck row by (Entity, Key, DueTimeUtc)
  -> If retry is possible, persist Pending with the normal exponential backoff
  -> Otherwise, persist Failed or Expired
  -> Refresh the pending overview and ack-timeout timer
  -> Return the durable result to the caller
```

The negative acknowledgement uses the same attempt count, deadline, and backoff policy as an ack timeout.
It does not create a second retry budget.

### Occurrence status query

`IReminderClient.GetOccurrenceStatusAsync` returns active and terminal state for one occurrence.
The query includes the attempt count, failure reason, next attempt, deadlines, and completion state.
Terminal results remain available until normal pruning removes the row.
All `IReminderStorage` providers must support the query.

## Threat Model

The primary threat is **asymmetric database failure**: reads succeed but writes fail.

### Why total database failure is safe

If the database is completely unavailable, the due-reminder fetch fails before any reminders are delivered.
No delivery occurs and the scheduler retries on a later tick.

### Why asymmetric failure used to be dangerous

The historical failure mode was:

1. Fetch succeeds
2. Delivery succeeds
3. Persistence of delivery state fails
4. The same reminders remain Pending
5. The next tick re-fetches and re-delivers them

That created duplicate-delivery storms under write pressure.

### Current mitigation

Delivery-state writes now happen **before** user messages are sent.

- If those writes fail, the scheduler opens the write circuit and sends nothing.
- The first-failure duplicate blast radius for delivery-state write failure is therefore zero.
- While the circuit is open, the scheduler probes with a single reminder before resuming full batches.

## Important Failure Modes

### Ack lost or recipient crashes before acking

- The occurrence remains `AwaitingAck`.
- Once `AckTimeout` elapses, the deadline-driven `CheckAckTimeouts` timer fires.
- The scheduler retries with exponential backoff (`RetryBackoffBase`, capped by `MaxRetryBackoff`).
- Retries stop when the occurrence would exceed its deadline or `MaxDeliveryAttempts`.

### Ack buffer flush fails

- All buffered senders receive an `Error` response. As with other storage errors, the write may have landed.
- If the write did not land, the occurrence stays `AwaitingAck`; `CheckAckTimeouts` re-encounters it and retries after the ack deadline. The consumer may receive a duplicate; idempotency handles this.
- If storage accepted the acknowledgement but its reply was lost, the occurrence remains `Delivered` and must not be delivered again unless an explicit new schedule replaces its state.
- Reaching the acknowledgement call is not proof of acceptance: an ineligible or superseded occurrence can return `NotFound`. Losing that reply does not turn it into an acknowledged occurrence.

### Delivery-state commit lands but reports failure

- A commit can reach the database while the scheduler sees an error (a dropped connection, or `StorageTimeout` firing during `COMMIT`). Storage can report this by returning false or by throwing. The scheduler cannot tell a landed commit from a failed one, so each of the three paths below plans for the landed case.
- **Delivery commit:** the rows may be `AwaitingAck` with nothing sent. The scheduler arms the ack-timeout check from that chunk's ack deadlines. The normal ack-timeout path then finds and retries the rows. If the commit really failed, the check finds nothing, refreshes from storage and cancels itself, or re-arms at the next real deadline if other rows await an ack.
- **Ack-timeout commit:** the retries may be `Pending` in storage. The scheduler re-arms the ack-timeout check at `StorageTimeout * 2`. A check that processes rows or encounters a failure reloads the pending overview and arms the fetch timer, so a landed retry is fetched. An empty successful check avoids this overview read.
- **Negative acknowledgement commit:** the retry may be `Pending` in storage. The scheduler reloads the overview and arms the fetch timer, then still replies `Error`. A commit that throws gets the same handling.
- If a pending-overview reload fails, the scheduler logs a warning and retains a recovery deadline at `StorageTimeout * 2` through the existing `FetchReminders` timer. Recovery is independent of ack-timeout tracking, so an unrelated acknowledgement cannot cancel it. Successful authoritative pending reads clear that recovery deadline.
- Costs: a delivery-commit retry goes out one ack timeout late, an unsent attempt counts as one delivery attempt, and with `MaxDeliveryAttempts = 1` the row can end `Failed` without ever being sent. If the database keeps failing, the write circuit limits fetches to one reminder at a time but adds no delay; see "Write circuit breaker".

### Scheduler restart / singleton handoff

- Awaiting-ack state is stored in the database, not only in memory.
- On startup, the scheduler loads the next ack deadline from storage as part of `InitResult` and schedules the timeout check before processing any messages.
- Late acks remain safe because they are matched by `DueTimeUtc`.

### Storage read failure and automatic recovery

The normal recovery path catches a storage failure and schedules another processing or recovery tick.
Repeated failures must be paced with a finite, nonzero retry delay rather than causing a loop of immediate
failed reads. If a failure instead causes a supervised actor restart, automatic reinitialization is
also an acceptable recovery path.

Neither path may strand durable work. Once storage is healthy and the scheduler can process messages,
it must automatically resume eligible `Pending` occurrences and recover `AwaitingAck` occurrences
through the normal timeout path. Recovery must not require a new client command or a manual restart.
Completing unrelated work, such as acknowledging another occurrence, must not suppress recovery of
work that still needs to be rediscovered after a failed read.

Recovery preserves occurrence identity, attempt counts, retry backoff, and deadlines. It must not
reset retry budgets, reopen terminal occurrences, or replay a backlog of expired recurring slots.
The normal expiry and latest-only rules still apply when storage returns.

Storage failures and restarts may delay processing. There is no fixed wall-clock delivery bound during
these failures. The behavior model must check recovery after a sufficient healthy processing period,
accounting for further failures or scheduler stalls during that period. Its recovery allowance is a
test observation window, not a production delivery guarantee.

### Scheduler lag longer than the repeat interval

Restarts, failover, thread-pool starvation, GC pauses, or slow storage can delay the scheduler past
a recurring occurrence's deadline before it is delivered.

- Storage expiry leaves `Pending` recurring occurrences alone, and the fetch and overview queries keep
  returning them, so the scheduler still sees them (including right after a restart).
- The scheduler marks the stale occurrence `Expired` (never delivered late) and writes the next live
  slot in the same commit: `due + k * interval`, with `k` the smallest value whose deadline is after now.
- A lag of many intervals produces one occurrence, not a backlog.
- Each commit chunk reads the clock once, so a slow earlier chunk cannot make a later one deliver
  past its deadline according to that chunk's classification time.
- Deadline eligibility is judged when the chunk prepares its durable mutations, before awaiting the commit. A slow commit response can delay the actual send beyond the deadline; there is no second deadline check after a successful commit. The envelope retains its absolute deadline so consumers can decline stale side effects.

### Stored payload can no longer be deserialized

A message type was renamed or removed, or its serializer is gone. The SQL providers handle such a
row during the fetch, so it cannot block other reminders:

- Past its delivery deadline: marked `Failed` (for a recurring reminder, the series ends there).
- Still inside its deadline, or with no deadline: logged as an error, skipped for this fetch, and
  left `Pending`, so fixing the type mapping recovers it. The fetch pages past skipped rows, and the
  overview it returns leaves them out, so they cannot cause a loop of immediate re-fetches.

### Late ack for superseded recurring occurrence

- The old occurrence is already expired or no longer AwaitingAck.
- The scheduler returns `NotFound`.
- The newer occurrence is unaffected.

### Ack and negative acknowledgement race

- The scheduler processes both commands through one mailbox.
- A buffered ack that arrived first is flushed before the negative acknowledgement.
- The first durable transition wins.
- A stale ack or negative acknowledgement returns `NotFound`.
- A newer occurrence with another `DueTimeUtc` remains unaffected.

### Negative acknowledgement write fails

- The scheduler returns `Error` to the caller.
- If the write really failed, the occurrence remains `AwaitingAck` and the normal ack-timeout path will retry it later.
- If the write landed but reported failure (returned false or threw), the retry (or terminal result) is already in storage. The scheduler reloads the overview and arms the fetch timer, so a `Pending` retry is still delivered after its backoff. The caller still sees `Error`.

### Restart after a negative acknowledgement

- A retry remains `Pending` in durable storage.
- A terminal result remains `Failed` or `Expired` until pruning.
- Scheduler initialization restores the pending overview and next timers.

### Reminder becomes stale in the mailbox

- The envelope carries `Deadline` to user code.
- Consumers can call `envelope.Deadline.IsExpired()` before doing side effects.
- Even if the consumer declines to act, it should still ack to stop useless retries.

## Backpressure and SQL Design

### 1. Bounded batch size (`MaxBatchSize`)

Due-reminder fetches use `TOP` / `LIMIT` so the scheduler never attempts to process the entire backlog in one query.

### 2. Chunked delivery commits (`DeliveryCommitChunkSize`)

Each fetched batch is processed in smaller chunks. This bounds the amount of state the scheduler tries to persist per write phase.

### 3. Batched SQL writes

Hot-path writes use batched statements in one atomic commit:

- **Delivery path**: `CommitReminderMutationsAsync` handles pending upserts (retries + next recurring occurrences), terminal completions, and awaiting-ack transitions in a single call per chunk.
- **Ack path**: `AcknowledgeRemindersAsync` flushes buffered acks in batches of `AckFlushBatchSize`.

This avoids a separate storage operation per reminder in both the delivery and acknowledgement paths; one commit may execute multiple SQL statements. A fetch pass that prepares insert-only successors finishes with a bounded fetch to reconcile the actual pending overview; there are no per-reminder neighbour queries.

### 4. Pending overview excludes AwaitingAck

Pending-overview queries only count actionable `Pending` rows.

- `AwaitingAck` rows are not treated as pending work.
- Rows past their deadline are excluded, except `Pending` recurring occurrences: the scheduler still
  needs to see those to expire them and roll the series forward.
- This prevents hot empty-fetch polling while the system is simply waiting for acks.

### 5. Incremental overview maintenance

The scheduler maintains the `ReminderOverview` incrementally during batch processing by applying each upserted reminder to the in-memory overview. A full overview aggregate reload happens when a fetch or write fails. Conditional successor inserts and active-only completions can differ from the proposed mutations, so a short batch containing insert-only successors uses one final bounded fetch to reconcile actual pending work. This avoids per-reminder queries while keeping the next timer authoritative.

Only rows that stay `Pending` are applied. A row the same commit ends (`Failed` or `Expired`) is written with its final attempt count but is not pending work, so it is left out. An empty overview is `TimeUntilNext = TimeSpan.MaxValue`; zero means "due right now" and is never treated as empty.

### 6. Write circuit breaker

When any hot-path write fails (in either `ProcessReminders` or `ProcessAckTimeouts`):

- The circuit opens.
- The current run stops.
- Later runs probe with a single reminder.
- Once the probe succeeds, the scheduler resumes full-batch processing in the same run.

## Accepted Trade-offs

### At-least-once remains the contract

This system still allows duplicates during normal distributed failure modes.
The design goal is to keep those duplicates bounded and occurrence-specific.

### Recurring reminders are latest-only, not catch-up

If an old recurring occurrence is still unacked when the next occurrence is delivered or becomes due, the old one expires instead of building an unbounded replay backlog. A late ack or nack for it is a `NotFound`.

Successor creation and supersession are batched conditional mutations, with no extra per-occurrence read.

### Deadline expiration is best-effort cleanup

The scheduler marks expired rows terminally during each tick (as a prelude to fetching), but correctness does not depend on cleanup running first.
Fetch and ack paths also enforce the deadline directly.

### Ack writes are eventually consistent

Acks are buffered in memory and flushed in batches rather than written per-ack. This trades immediate durability for throughput. If the scheduler crashes between receiving an ack and flushing it, the occurrence stays `AwaitingAck` and will be retried after timeout — which is the same outcome as if the ack message had been lost in transit.

### Custom storage provider compatibility

Version 0.7 extends `IReminderStorage` with exact occurrence queries.
Custom providers must implement these members before they upgrade.

The new commands have new Akka serializer manifests. Existing manifests keep
their 0.6 layouts. During an upgrade, deploy the 0.7 scheduler before consumers
call `NackAsync` or `GetOccurrenceStatusAsync`.
Negative acknowledgement uses the existing durable mutation contract.

### Poison recurring reminders

`MaxDeliveryAttempts` applies to one occurrence. Each recurring occurrence starts with zero attempts.
A terminal occurrence does not cancel or disable the recurring reminder definition.

### Custom storage providers

The unreleased 0.7 recurring scheduler requires `IConditionalReminderMutationStorage`. Implementations must apply
`ReminderMutationBatch.PendingInserts` without updating existing rows and `ActiveCompletions` only to Pending or
AwaitingAck rows, atomically with the original mutation lists. Inserts run before ordinary upserts, and active
completions before AwaitingAck transitions. Ordinary `PendingUpserts` retain their existing overwrite behavior.
Forwarding providers must preserve these lists and declare the marker only when their underlying provider supports
the contract. One-off reminders continue to support plain `IReminderStorage`; unsupported recurring schedules
return an error before persistence, and previously stored recurring rows are not delivered through unsupported providers.

Upgrade a custom provider before starting the scheduler against a store containing recurring reminders. Existing
recurring rows on an unsupported provider cannot be committed or delivered, and can block later one-off work in
that store; accepting a new one-off schedule does not remove this compatibility requirement.
