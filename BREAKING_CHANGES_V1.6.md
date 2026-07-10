# Akka.NET v1.6 Breaking Changes

This document tracks **breaking changes** introduced into the `dev` branch during the
Akka.NET **v1.6** development cycle, ahead of a stable `v1.6.0` release.

A change is **breaking** — and therefore **must** be recorded here — if it alters any of:

- **Behavior** — observable runtime semantics change (e.g. a message that used to do `X`
  now does `Y`, or is now ignored).
- **Wire** — serialization or network-protocol compatibility with prior `1.x` releases
  changes.
- **API** — a public type or member is removed, renamed, or has its signature/contract
  changed (anything `Akka.API.Tests` would flag).

## Scope and lifecycle

- This ledger is maintained **only until a stable `v1.6.0` ships**. At that point its
  contents are folded into the official release notes / upgrade guide and this file is
  retired.
- Record the entry in the **same PR** that introduces the breaking change, so reviewers can
  weigh the break alongside the code.
- A change still in flight may be listed with status `Planned` and its branch name; update
  it to `Merged` with the PR link once it lands on `dev`.
- Bug fixes that merely restore documented/intended behavior are not "breaking" for this
  list — but note them anyway if users may have come to depend on the old (incorrect)
  behavior.

## Entry format

Newest first. Keep `Change` to a line or two; link the PR for detail. `Type` is one or more
of `Behavior`, `Wire`, `API` (combine with `+`).

| Status | PR / Branch | Component | Type | Change | Migration |
|--------|-------------|-----------|------|--------|-----------|
| Planned / Merged | link or branch | `Akka.Xyz` | Behavior | one-line summary | what users must do |

---

## Changes

| Status | PR / Branch | Component | Type | Change | Migration |
|--------|-------------|-----------|------|--------|-----------|
| Planned | `fix/cluster-nonblocking-startup` | `Akka.Cluster` | Behavior | Cluster extension startup no longer blocks on an internal 20s ask bounded by `akka.actor.creation-timeout`; `Cluster.Get()` returns immediately and core initialization completes asynchronously. The "Failed to startup Cluster" ask-timeout failure mode (which could terminate a healthy node under thread starvation) is removed; genuine core-startup failures still shut the node down via supervision. | None for typical code — all public APIs are message-based or awaitable. Code that used `Cluster.Get()` as a "core is started" synchronization point should await `JoinAsync`/`RegisterOnMemberUp` or observe cluster state. |
| Planned | `fix/artery-pipe-watermarks` | `Akka.Remote` (Artery) | Behavior | Artery TCP connections now raise the pause/resume watermarks of their internal read pipe to 1 MiB (pause 2 MiB), via a new opt-in `Akka.IO.Inet.SO.PipeBufferSize` socket option consulted by `TcpIncomingConnection`/`TcpOutgoingConnection`, configurable via `akka.remote.artery.advanced.tcp.pipe-buffer-size` (default `1m`). Akka.IO's own default (~16 KiB, derived from `akka.io.tcp.receive-buffer-size`) is unchanged for every other TCP connection. | No action required. Artery connections use somewhat more memory per connection and see higher throughput under high-in-flight/one-way traffic; tune via `akka.remote.artery.advanced.tcp.pipe-buffer-size` if needed. |
| Merged | [#8317](https://github.com/akkadotnet/akka.net/pull/8317) | `Akka.Remote` / `Akka.Cluster` | API | System/address UID widened `int` → `long` across `AddressUid.Uid`, `AddressUidExtension.Uid()`, `UniqueAddress(Address, int)`/`.Uid`, `QuarantinedEvent(Address, int)`/`.Uid`, `RemoteWatcher.HeartbeatRsp(int)`/`.AddressUid`, and all `Quarantine(Address, int? uid)` members (`IRemoteActorRefProvider` / `RemoteTransport` / `RemoteWatcher`). Hard re-type (v1.6). Prerequisite for Artery. | Recompile against the `long` members; replace `int` uid locals with `long`. Default UID generation stays in 32-bit range; full 64-bit generation is opt-in via `akka.remote.use-64bit-system-uids = on`. |
| Merged | [#8317](https://github.com/akkadotnet/akka.net/pull/8317) | `Akka.Cluster` | Wire | Cluster gossip `UniqueAddress.uid` widened `uint32` → `uint64` (`ClusterMessages.proto`). Same varint wire type → binary-compatible for uids ≤ `uint32.MaxValue`. | Rolling upgrade safe provided uid generation stays in 32-bit range (the default); enable `akka.remote.use-64bit-system-uids` only after the whole cluster + all remote peers are on v1.6. |
| Planned | `fix/artery-queue-and-shutdown-robustness` | `Akka.Remote` (Artery) | Behavior | Artery's control-queue default capacity raised from 256 to 20000 (matches Pekko's `outbound-control-queue-size`) to stop a mass-`Unwatch` termination burst from spuriously quarantining a healthy peer. Local-only (per-association channel size); no wire-format impact. | Configurable via `akka.remote.artery.advanced.outbound-control-queue-size` if a different capacity is needed. |
| Planned | `fix/artery-queue-and-shutdown-robustness` | `Akka.Remote` (Artery) | Behavior | Artery's ordinary-outbound-queue overflow now publishes a `Dropped` event directly to the `EventStream` instead of routing through `DeadLetters.Tell` (which double-wrapped it as a `DeadLetter`). | Subscribers watching for dropped Artery sends should subscribe to `Dropped`, not `DeadLetter`; the default `DeadLetterListener` already logs both. |
| Planned | `feature/deprecate-actorpublisher` | `Akka.Streams` | Behavior | `Source.ActorRef<T>` is re-implemented as a stream-native `GraphStage` (off the legacy `ActorPublisher`). Its public signature is unchanged, but the materialized `IActorRef` no longer treats `PoisonPill` / `Kill` as stream completion — those messages are now ignored. (`Status.Success` draining and `Status.Failure` completion are unchanged.) | Complete the stream explicitly: send `new Status.Success(...)` to drain buffered elements and complete, or `new Status.Failure(ex)` to fail. Do not rely on `PoisonPill` / `Kill`. |
