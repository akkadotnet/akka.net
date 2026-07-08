## 0. Design Review Gate (do FIRST — see design.md "Open questions")

- [ ] 0.1 Review the **advertisement protocol** end-to-end (receiver builds/advertises → sender installs/acks → receiver confirms/rotates)
- [ ] 0.2 Decide **Q1 ownership/threading**: stage-owned async-callback (Pekko-faithful) vs registry-owned per-UID with lock-free decompression reads (recommended). Confirm inbound decode is a single point before lane fan-out
- [ ] 0.3 Decide **Q2** (both confirmation triggers vs Ack-only), **Q4** (frequency-sketch fidelity), **Q5** (immutable `Volatile` outbound swap), **Q7** (observability/events for tests)
- [ ] 0.4 Confirm **Q3/Q6** (KeepOldTables=3, 127→0 wrap, unknown-version drop+re-advertise, no send-side barrier) are ported verbatim
- [ ] 0.5 Record decisions back into design.md before writing protocol code

## 1. Compression Tables (SCAFFOLDED — verify + test)

- [x] 1.1 `CompressionTable<T>` (`value→index`, `Compress`, `Invert`, `Empty`)
- [x] 1.2 `DecompressionTable<T>` (`index→value`, `Get`, `Empty`, `Disabled@0xFF`)
- [x] 1.3 `CompressionTagCodec` encode/decode hooks (`MakeCompressedTag`, `TryBuild*Tag`, `TryResolve`)
- [x] 1.4 `ArteryEnvelopeHeader.CompressedTagMarker` constant (additive)
- [ ] 1.5 Unit tests: `Invert` round-trip, dense-index inversion, `Compress` miss = -1, `Get` out-of-range throws, `MakeCompressedTag`↔`ClassifyTag`/`DecodeCompressedIndex` agreement, index-space bounds (0..65 535)

## 2. Heavy-Hitter Detection

- [ ] 2.1 `IFrequencySketch<T>` seam + `TopHeavyHitters<T>` (bounded top-N by count)
- [ ] 2.2 MVP bounded count-based sketch (per Decision 6)
- [ ] 2.3 (later, behind the seam) port Pekko `FastFrequencySketch` (TinyLFU aging); select via `frequency-sketch-implementation`
- [ ] 2.4 Exclusions: temporary/promise actor refs, empty manifests
- [ ] 2.5 Unit tests for top-N eviction and exclusions

## 3. Inbound Compression State Machine

- [ ] 3.1 `InboundCompression` per-origin table rotation (`activeTable`/`nextTable`/`oldTables≤3`/`advertisementInProgress`)
- [ ] 3.2 `selectTable(version)`: active → old → in-progress-flip → unknown(drop) (verbatim from Pekko)
- [ ] 3.3 `ConfirmAdvertisement` → `StartUsingNextTable` (rotate + `127→0` wrap)
- [ ] 3.4 `RunNextAdvertisement`: build from hitters, mark in-progress, resend≤3, give-up-flip
- [ ] 3.5 `InboundCompressionsImpl` demux by origin UID (`ConcurrentDictionary<long,…>`, create-on-demand); replace `NoInboundCompressions` when enabled
- [ ] 3.6 `Close(originUid)` on quarantine / dead peer
- [ ] 3.7 Ownership/threading per Q1 decision (lock-free decompression read; short lock for hitter mutation + table build)
- [ ] 3.8 Unit tests: rotation, old-table retention decode, unknown-version drop, give-up path

## 4. Control-Stream Advertisement Protocol

- [ ] 4.1 Promote the four `CompressionProtocol` records to `IArteryControlMessage` + `[AkkaSerializable]`
- [ ] 4.2 MessagePack V2 manifest constants in `ArteryControlMessageSerializer` (ordered `Table` list per Decision 5)
- [ ] 4.3 Serializer round-trip tests (add to `ArteryControlMessageSerializerSpec`)
- [ ] 4.4 Dispatch advertisements/acks (`IControlMessageSubscriber` or `ArteryRemoting.ControlMessageReceived`)
- [ ] 4.5 On advertisement: swap outbound table on `Association` + reply Ack
- [ ] 4.6 On Ack (and first stamped message): `ConfirmAdvertisement`
- [ ] 4.7 Send via `EnqueueControl`; skip quarantined origins; never compress `ArteryMessage`
- [ ] 4.8 Per-origin advertisement scheduler (`Scheduler` tick at `advertisement-interval`)

## 5. Outbound Encode Integration

- [ ] 5.1 Add `_outboundActorRefTable` / `_outboundManifestTable` (`volatile`, immutable) to `Association`
- [ ] 5.2 Thread the outbound-compression source into `ArteryEncodeStage` at materialization (`ArteryRemoting.cs:659`)
- [ ] 5.3 `Encode` overload consults `CompressionTagCodec.TryBuild*Tag`; **stamp version byte**; COMPRESSED on hit, LITERAL on miss
- [ ] 5.4 Force LITERAL for control/`ArteryMessage` regardless of table
- [ ] 5.5 **FLIP POINT**: only now does encode emit COMPRESSED — gate behind `compression.enabled`
- [ ] 5.6 Encode unit tests: hit→COMPRESSED+version, miss→LITERAL, control→never compressed

## 6. Inbound Decode Integration

- [ ] 6.1 Thread `IInboundCompressions` into `ArteryInboundProcessingStage` (ctor at `ArteryRemoting.cs:353`)
- [ ] 6.2 Replace COMPRESSED-recipient/sender drop (`:234-240`) with `TryDecompressActorRef(originUid, version, idx)`
- [ ] 6.3 Replace COMPRESSED-manifest drop (`:223-227`) with `TryDecompressClassManifest(...)`
- [ ] 6.4 Record sampled hits (sender/recipient path, manifest) against origin UID after LITERAL decode
- [ ] 6.5 Miss → drop-with-warning (no stream fault) + let re-advertisement recover
- [ ] 6.6 Decode unit tests incl. stale/unknown version

## 7. Configuration

- [ ] 7.1 `artery.advanced.compression` HOCON defaults in `Remote.conf` (enabled=off, max=256, interval=1m, sketch impl)
- [ ] 7.2 Parse + validate in `ArterySettings` (enforce `max ≤ 65 535`)
- [ ] 7.3 Disabled path installs `NoInboundCompressions` + `Empty` outbound table (byte-identical wire)
- [ ] 7.4 `ArteryConfigSpec` coverage

## 8. Integration + End-to-End

- [ ] 8.1 Two-system spec: warm up → observe advertisement → confirm subsequent messages carry COMPRESSED tags and decode correctly
- [ ] 8.2 Restarted-incarnation spec (equivalent of Pekko `HandshakeShouldDropCompressionTableSpec`): unknown version dropped, fresh table advertised, recovery
- [ ] 8.3 Interop spec: compression-enabled node ↔ non-advertising peer stays on LITERAL
- [ ] 8.4 Quarantine: advertisement stops, tables closed
- [ ] 8.5 **MNTR**: run affected multi-node Remote/Cluster specs (message re-type/type-erasure only surfaces at MNTR)
- [ ] 8.6 API approval (`Akka.API.Tests`) — internal types only; confirm no public surface change
- [ ] 8.7 Benchmark (naked, baseline-first) RemotePingPong Artery vs pre-compression baseline; record allocation delta
- [ ] 8.8 `BREAKING_CHANGES_V1.6.md` entry if any observable/config change qualifies
