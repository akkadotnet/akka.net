# Model-based tests for the reminder scheduler

These tests use [CsCheck](https://github.com/AnthonyLloyd/CsCheck). CsCheck makes up random lists of operations, runs each list against a real `ReminderScheduler` on a virtual clock, and compares what the application saw with a model. When a list fails, CsCheck looks for a shorter one that still fails and prints it with a seed.

Read the full async test in this order:

1. `ReminderFaultSpecs.RunAsync`: execute a command, pass the recorded observations to the model.
2. `ReminderCommands.cs`: the 13 application commands and their direct calls to `ReminderApp`.
3. `ReminderModel.Observations.cs`: check replies and apply observations to the model.
4. `ReminderModel.cs`, `Liveness.cs`, and `SafetyRules.cs`: occurrence state and the guarantees below.

```csharp
foreach (var command in commands)
{
    await command.Run(app);
    model.Observe(app.Journal.Read());
}
```

The model has no live application reference. `Observe` takes the cumulative journal history, not just
the newest events; it tracks how much it has already checked. Each run has its own model and application. A `finally`
block stops the application and awaits cleanup on success, on failure, and while CsCheck shrinks.

`InMemoryManualAcknowledgements` retains the former healthy model's five-command profile, timing
ranges, and silent recipients with per-entity acknowledgements. It uses the same async runner and model
as the fault tests; the separate `ReminderSpecs` model and runner have been removed. Its guarantees
are checked by `DeliveredOnTime`, `AckedNeverRedelivered`, `NothingAfterCancel`, and `LatestOnly`.
`RequireHealthyDeliveries` also retains the independent per-registration delivery check: an earlier
payload delivered at the same due time cannot satisfy a replacement registration.
The full model also checks schedule replies and retry obligations after each command.

`RecurringSpec.cs` and `RecurringConformanceSpecs.cs` provide a bounded native `Spec`:
explicit guards and rules, exhaustive model exploration, and sampled scheduler conformance. Its inputs have semantic types: `FirstDueTime` (`DueNow` or `Overdue`), `Occurrence`, and
`OccurrenceCount`. `WaitFor(1 occurrence)` advances to the next occurrence, not by one second.
`ExpectedDeliveries` predicts how many distinct occurrences have arrived; acknowledgement state
identifies an occurrence, not a count. Nullable values mean no schedule or acknowledgement yet. Its batch-one eager-fetch setup is specific
to that example; early delivery is permitted by slippage, not universally required.

## What CsCheck owns

- The full suite uses `Gen.Frequency`, the existing argument generators, and native `.Array` to generate
  arbitrary command lists. The array length domain is the same 0..127 as `SampleModelBasedAsync` used.
- `SampleAsync` executes those lists, shrinks failures, prints the generated inputs, and owns seed replay
  and sampling controls. No custom seed or shrinking machinery is used.
- All 13 command kinds, weights, settings, broad timing ranges, and fault combinations remain available.
  The generated fault-free variant excludes only `SetRegion` and `InjectFault`, as before.
- Fixed regressions append `HealAndWait` to check recovery. Generated lists have no mandatory scenario
  prefix or recovery suffix; `HealAndWait` is one of their freely chosen commands, as before.

The full suite does **not** use native `Spec.Conform`: that runner generates the whole pure model trace
before executing the scheduler. Our expectations depend on observed early deliveries, fault activation,
lost responses, and elapsed recovery time. A fixed prediction would reject valid outcomes. Capturing a
mutable application in a Spec transition would undermine the model. `ReminderModel.Observe` instead
checks each observation against prior model state and retains the existing uncertainty rules.

The command-list property has no generation-time precondition API. This is intentional: cancellation of
absent keys and late acknowledgements must remain testable. `AckOutstanding` with nothing to acknowledge
is a harmless no-op. The application-specific model supplies the behavioral guarantees; CsCheck supplies
generation and shrinking.

## Replay across generator changes

A seed identifies a case only for the exact generator composition that produced it. Switching from
`SampleModelBasedAsync` to the direct `SampleAsync` property changes that composition, even though the
argument domains and weights are preserved. Old full-model seeds are not interchangeable with new ones.
Use the emitted seed with `CsCheck_Iter=1` on the same generator, or retain concrete commands in
`ModelRegressionSpecs.cs` when changing generators. The past-anchor regression pins the earlier
one-command superseded-ack failure for this reason.

Commands now print using their record's normal representation, for example `Tick { Ms = 9000 }`.
That is readable diagnostic output, not a promise of a pasteable C# constructor. A fixed regression uses
`new Tick(9000)`. Run negative controls in an isolated checkout of the historical scheduler, with the
matching test-harness storage interface; do not overwrite a working checkout's runtime to run a demo.

"docs" below is `docs/design/failure-modes.md`. "Ruling" is a maintainer decision recorded in PR #149.

## Run it

```bash
# local model tests (SQL providers run separately)
dotnet test src/Akka.Reminders.Tests -c Release --filter "FullyQualifiedName~Akka.Reminders.Tests.Model&Category!=ModelSql"

# more lists per test
CsCheck_Iter=5000 dotnet test src/Akka.Reminders.Tests -c Release --filter "FullyQualifiedName~ReminderFaultSpecs.InMemory"

# PostgreSQL and SQL Server too (needs Docker)
REMINDERS_CSCHECK_SQL=1 dotnet test src/Akka.Reminders.Tests -c Release --filter "Category=ModelSql"
```

Defaults: 100 lists for manual acknowledgements (CsCheck's own default), 120 in-memory with faults, 60 in-memory with healthy storage, 30 SQLite. A list has up to 127 operations.

When the fault layer fails, it prints settings and commands. To keep the case across generator changes, add the concrete inputs to `ModelRegressionSpecs.cs`. To shrink further on the same generator, run again with the printed seed and a larger `CsCheck_Iter` or `CsCheck_Time` budget.

## Words

- **Occurrence**: one due time of a reminder. Its **deadline** is `due + MaxDeliveryWindow`, or the next due time if that is sooner. No window and no interval: no deadline.
- **Awake**: the scheduler is keeping up. It is awake except during a `Lag`.
- **Trouble**: a storage call the test made fail or run slow, or a shard region the test took down. A slow call is over when it returns. A failed call or a missing region is over `RecoveryTime` later. A stall or a slow call during that wait starts the wait again when it ends.
- **RecoveryTime** = `AckTimeout + MaxRetryBackoff + 2 × StorageTimeout`. This is the test's observation allowance for an ack timeout, retry backoff, and a recovery tick. Later faults or stalls extend it. It is not a production wall-clock delivery guarantee (docs: Storage read failure and automatic recovery).

## Commands and checks

Command generation is in `ReminderFaultSpecs.cs`, execution is in `ReminderCommands.cs`, and postconditions are in `ReminderModel.Observations.cs`. Trouble changes what the model can know about a result and when recovery is owed; the established allowances below remain unchanged.

| Operation | Precondition | Effect on the model | Postcondition |
|---|---|---|---|
| `ScheduleOnce`, `ScheduleRecurring` | none | The new reminder replaces any reminder under the same entity and key. Nothing changes if the shard region is down. | Reply is `Success`, or `ShardRegionNotFound` if the region is down. `Error` only if the save itself failed. Acceptance is unknown to the caller; if the injected fault is `AppliedThenFail`, the test knows it persisted and requires automatic recovery. Otherwise the model stops asking anything of that key. |
| `Cancel`, `CancelAll` | none | The reminder ends (all reminders of the entity for `CancelAll`). | Reply is `Success` if there was work left, `NotFound` if not. `Error` only if a storage call failed; then the model stops asking anything of those keys. Not compared after trouble. |
| `ListReminders` | none | none | Shows exactly the keys that have work left, each with the payload of the live schedule call. `Error` only if the read failed. Not compared after trouble. |
| `Tick(ms)` | none | Time passes; the scheduler is awake all the way. | Liveness (below). |
| `Lag(ms)` | none | Time jumps; the scheduler is awake only at the end. Occurrences whose deadline passed are skipped. | Liveness. |
| `Restart` | none | none | Liveness: a restart changes nothing the application can see. |
| `SetRecipient(entity, mode)` | none | none. The entity now acks, nacks or stays silent on each delivery. | Each ack: `Success` if the occurrence was awaiting an ack, else `NotFound`. Each nack: `RetryScheduled` with retry time `now + backoff`; or `Failed` (attempts used up); or `Expired` (the retry would pass the deadline); or `NotFound`. Any reply after trouble. |
| `AckOutstanding` | a silent recipient left deliveries unanswered | As each ack. | As each ack. |
| `SetRegion(region, up)` | none | The region is up or down. | Starts or ends trouble for the region's entities. |
| `InjectFault(call, kind, …)` | none | none | When the fault fires it starts trouble. Kinds: `Fail`, `Timeout`, `Slow`, `AppliedThenFail` (the write lands but reports failure). |
| `HealAndWait` | none | All regions are up. | Storage is healthy again; `RecoveryTime` passes if there was any trouble, then 30 s more. Liveness then holds with nothing left to excuse. |

An occurrence is **awaiting an ack** when it was delivered, less than `AckTimeout` ago, it is not yet acked or nacked, its deadline has not passed, no newer occurrence of the same reminder was delivered, and the reminder was not cancelled or replaced (`ReminderModel.PhaseOf`).

An acknowledgement can also persist before its response is lost. Injected ack faults record their submitted
identities, so overlapping error replies cannot borrow another flush batch's landed write. For an
exactly targeted `Ack` `AppliedThenFail`, the model retains a durable acknowledgement and the independent
safety rule rejects a later delivery from the same registration when observations establish eligibility:
before its timeout and occurrence deadline, without intervening trouble, nack, replacement or supersession.
Reaching storage alone is insufficient: a late ack can return `NotFound` and leave a pending retry unchanged.
If earlier trouble makes acceptance unknowable, only that occurrence is marked `AcceptanceUnknown`. Either another
delivery or silence is permitted; an observed delivery clears the ambiguity and restores its normal retry
obligation. A successful nack also resolves the uncertainty. This does not put the key or future recurring
occurrences in doubt. Independently ineligible acks after healthy timeout processing retain the retry obligation;
an input buffered before that timeout retains acceptance uncertainty when its later flush reports failure.
Explicit re-registration can reopen the same due time. An ack that failed before persistence still
requires timeout recovery. This distinction uses the injected fault, not storage rows or scheduler state.

### Liveness: what must have arrived

Checked after every operation (`Liveness.cs`).

| Rule | Statement | Source | Code |
|---|---|---|---|
| DeliveredOnTime | Every occurrence is delivered at the first moment the scheduler is awake at or after its due time (up to `MaxSlippage` early), unless its deadline has passed by then. | docs: Scheduler tick; Scheduler lag longer than the repeat interval; Meaning of a scheduling error; Storage read failure and automatic recovery | `Liveness.DeliveredOnTime`, `ReminderModel.Require` |
| RetriedOnTime | A delivery that gets no ack within `AckTimeout` is sent again after the backoff, and a nacked one at the time the nack reply gave, while attempts and the deadline allow. | docs: Ack lost or recipient crashes before acking; Negative acknowledgement handler | `Liveness.RetriedOnTime`, `ReminderModel.PhaseOf` |

How trouble loosens them:

- **DeliveredOnTime**: an occurrence that was due during trouble is owed when the trouble is over, not before. It is not owed at all if by then its deadline has passed, the reminder was cancelled, its shard region was down, or `MaxDeliveryAttempts` sends were committed but reported failure (each one counts as an attempt; ruling).
- **RetriedOnTime**: a retry due during trouble is owed when the recovery observation window ends, extended by later overlapping trouble. A slow storage call can prevent the scheduler from noticing an elapsed ack timeout, so timeout backoff starts after that blocking call returns. A nack's already-promised retry time gets no additional backoff. Landed-but-failed commits may have consumed unobserved attempts: the oracle allows the largest capped backoff consistent with those faults, while healthy backoff remains exact. A prior read failure never permanently disables this check. Expired occurrences or exhausted attempts are still excused; a missing region may consume an unobservable attempt budget.

### Safety rules: what must never happen

Checked after every operation, trouble or not (`SafetyRules.cs`). Each check is a few lines over the event list.

| Rule | Statement | Source | Code |
|---|---|---|---|
| OnlyWhatWasScheduled | Every delivery matches a schedule call: same entity and key, and a due time that call asked for. | README: Schedule Single / Recurring Reminder | `SafetyRules.OnlyWhatWasScheduled` |
| NotEarly | Nothing is delivered more than `MaxSlippage` before its due time. | docs: Scheduler tick | `SafetyRules.NotEarly` |
| NotAfterDeadline | Nothing is committed for delivery at or after its deadline. | docs: Latest-only recurring reminders. Ruling: deadlines are judged at commit time. | `SafetyRules.NotAfterDeadline` |
| HonestEnvelopeDeadline | The deadline on the envelope is never later than the occurrence's deadline. | README: Acknowledgement Protocol | `SafetyRules.HonestEnvelopeDeadline` |
| AttemptCap | One occurrence is delivered at most `MaxDeliveryAttempts` times. | docs: Delivery Semantics | `SafetyRules.AttemptCap` |
| AckedNeverRedelivered | Once an ack succeeds or is known to persist before its reply is lost, that occurrence is never delivered again under the same registration. | Ruling | `SafetyRules.AckedNeverRedelivered` |
| NothingAfterCancel | After a cancel is answered, or a new schedule call for the same key succeeds, the old reminder delivers nothing. | README: Cancel Reminder | `SafetyRules.NothingAfterCancel` |
| LatestOnly | A recurring reminder never delivers an older occurrence after a newer one. | docs: Latest-only recurring reminders | `SafetyRules.LatestOnly` |
| LateAckIsNotFound | Once a newer occurrence is delivered, an ack for an older one does not succeed. | docs: Late ack for superseded recurring occurrence | `SafetyRules.LateAckIsNotFound` |
| NoRepeatWithoutCause | An occurrence is delivered again only `AckTimeout` after the attempt before, or after a nack and then at most `MaxSlippage` before the retry time the nack reply gave. | docs: Ack lost or recipient crashes before acking; Current mitigation | `SafetyRules.NoRepeatWithoutCause` |
| ListShowsNothingCancelled | `ListReminders` never shows a reminder that was cancelled or replaced. | README: List Reminders | `SafetyRules.ListShowsNothingCancelled` |

One tolerance: when a slow commit ends at the moment of a delivery, the delivery counts from when that commit began (`History.CommittedAt`). `NotAfterDeadline` and `NoRepeatWithoutCause` use it.

`ReminderApp` also fails a run if the scheduler does not answer (`SchedulerResponds`) or keeps working while time stands still (`GoesIdle`). `RuleSpecs.cs` tests the rules themselves with a hand-written history that breaks each one.

## Recovery regressions

- A save that landed but answered `Error` must recover eligible persisted work without another client command or a manual restart. `Should_DeliverStoredReminder_When_SaveSucceededButReportedFailure` pins this requirement; generated `AppliedThenFail` schedules also enforce it.
- A read failure may delay a durable retry or cause an automatic actor restart. `Should_SendTheRetryOnTime_When_OverviewReadsFailAfterAnAckTimeout` requires the retry within the model's healthy recovery observation window, accounting for subsequent trouble; it imposes no fixed deadline from the start of the scenario.
- `RuleSpecs` verifies that a missing retry fails after recovery, later failures extend the window, and exhausted ambiguous attempts do not acquire a new delivery guarantee. Attempt costs include commits before the first observed delivery, but never before the occurrence could exist. Lost acknowledgement responses preserve their durable outcome. It also distinguishes delayed timeout detection from an already-promised nack retry and checks both healthy and uncertain attempt backoff.

## Explicit re-registration

A new schedule call replaces active work for the same entity and key and can reset occurrence state,
including reopening an identity delivered by an earlier registration (docs: Meaning of a scheduling error).
The model permits delivery of the same due time under the new registration; expired, exhausted or
otherwise obsolete occurrences do not acquire an additional delivery obligation merely because an old
registration delivered them.
