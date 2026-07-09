## Why

Profiling Artery TCP on a quiet 9900X shows **~68% of per-message allocation is literal actor-path + manifest serialization**: for every ordinary message the Encoder writes the sender path, recipient path, and class manifest as length-prefixed UTF-8 (`Encoding.UTF8.GetBytes` per literal, in `ArteryEnvelopeCodec.cs`), and the matching Decoder allocates a `string` per literal on the way back. GC/allocation pressure sits on the throughput critical path, so removing these literals is the next throughput lever after write-coalescing.

The Artery envelope wire format was designed for this from day one: the sender/recipient/manifest tags are 32-bit (`AbsentTag=0`, a non-zero top byte marks COMPRESSED with a 16-bit table index in the low bits, otherwise the tag is a LITERAL body offset), and the fixed header already reserves an `ActorRefTableVersion` byte and a `ManifestTableVersion` byte. Today the codec **never emits COMPRESSED** and there is no compression table; the decoder can classify a COMPRESSED tag and extract its index but cannot resolve it. This change fills in the missing half.

This mirrors Apache Pekko's Artery compression (Apache 2.0), ported .NET-idiomatically. **The subtle part is the advertisement protocol** — the *receiver* builds the tables from observed traffic and advertises index↔value mappings back to the *sender* over the control stream — and this proposal exists to get that design reviewed **before** the protocol is built.

## What Changes

- Add versioned, per-direction compression tables for actor refs and class manifests:
  - **Outbound `CompressionTable<T>`** (`value → index`) held per association; read by the Encoder to emit a COMPRESSED tag + table-version byte for known refs/manifests, else LITERAL (unchanged).
  - **Inbound `DecompressionTable<T>`** (`index → value`) held per **origin UID**; used by the Decoder to resolve a COMPRESSED index back to an actor path / manifest, completing decode's deferred "resolve index → ref".
- Add heavy-hitter detection on the inbound path (frequency sketch + top-N) so the receiver learns which refs/manifests are worth compressing.
- Add the **control-stream advertisement protocol**: `ActorRefCompressionAdvertisement` / `ClassManifestCompressionAdvertisement` (receiver → sender) plus their `...Ack` replies (sender → receiver), serialized on the existing control stream. Tables are versioned (0..127, wrapping; `-1` = disabled), advertised on a schedule, confirmed by the first message stamped with the new version **or** by the explicit Ack, with a bounded set of old tables retained so in-flight messages using a superseded version still decode.
- Add an `artery.advanced.compression` settings block (enabled, per-category `max`, `advertisement-interval`, frequency-sketch selection) — off by default until the loop and tests exist.
- Complete the codec: an `Encode` overload that consults an outbound compression context (still emitting LITERAL until the whole loop lands — see Non-Goals) and a Decoder-side "resolve compressed index → value" hook backed by the per-origin decompression tables.

### What Does Not Change

- **The Artery envelope wire layout is unchanged** — the tag scheme and the two table-version header bytes were reserved for exactly this; no new envelope fields.
- LITERAL encode/decode remains the fallback for any ref/manifest not in the active table, and the ONLY path used for control/handshake (`ArteryMessage`) traffic, which is never compressed.
- Classic remoting is untouched. Artery is not wire-compatible with JVM Artery (this is an Akka.NET-internal wire), and this change does not attempt to be.
- No change to `SerializerV2` payload bytes.

## Capabilities

### New Capabilities

- `artery-compression`: receiver-driven, versioned actor-ref and class-manifest compression for Artery, with a control-stream table-advertisement protocol, per-origin decompression tables, and heavy-hitter detection — replacing hot-path literal path/manifest serialization with O(1) index lookups.

## Impact

- **Akka.Remote**: new `Akka.Remote.Artery.Compression` namespace (tables, inbound-compression tracker, protocol messages, heavy-hitter detection); Encoder/Decoder hooks; association state gains a swappable outbound table; the inbound stage owns per-origin decompression tables; `ArteryControlMessageSerializer` learns four new control messages; `ArteryRemoting` dispatches advertisements/acks.
- **Configuration**: new `artery.advanced.compression` section (off by default).
- **Performance**: removes the dominant per-message allocation for warm actor refs/manifests; adds bounded inbound bookkeeping (frequency sketch + top-N) and a low-frequency control-stream advertisement. Net effect validated by benchmark **after** the loop lands, not in this design phase.
- **Compatibility**: additive. A node that never advertises a table, or has compression disabled, keeps emitting/receiving LITERAL tags and interoperates with a compression-enabled peer unchanged.
