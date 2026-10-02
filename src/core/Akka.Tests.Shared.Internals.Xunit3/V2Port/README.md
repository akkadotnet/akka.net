# V2 port test kit

Test helpers for a serializer port (#8675, #8714): a legacy serializer gets a V2 twin at id + 40, with the same manifests.

1. Capture the legacy bytes before you change anything: derive from `V2PortSpec`, run
   `AKKA_GOLDEN_CAPTURE=1 dotnet test --filter "DisplayName~Legacy"`, and commit the `legacy/*.hex` files.
2. Write the V2 serializer. Capture its bytes the same way (`DisplayName~V2`), review `git diff`, commit.
   A normal run only compares, and fails with the byte rows side by side.
3. `V2PortSpec` needs `CreateLegacy`, `CreateV2`, `Cases` (a `V2PortCase` per manifest, deterministic values) and
   `Golden` (`GoldenBytes.For("GoldenBytes/X")`). Register both serializers in config; give the V2 row no bindings.
4. `MixedBindingPair.Create(name, v2Config, legacyConfig)` starts one V2 node and one legacy node;
   `AssertRoundTripsBothWays(cases, v2Id, legacyId)` checks both directions. It only takes config. Use
   `MixedBindingPair.BindTo("alias", types)` for explicit bindings, or the global switch's config once it exists.

`V2PortSpec` checks: id is legacy + 40 inside 40-79; manifest parity; V2 and legacy round trips (including the buffer
path and `SizeHint`); both ids decode through `Sys.Serialization.Deserialize`; the binding stays legacy while the V2 row
is read-only; the V2 id resolves with dynamic type loading off; golden V2 and legacy bytes.

The dynamic-type-loading check flips a process-wide switch. Put the spec in a collection that does not run in parallel
(`DynamicTypeLoadingCollection` in `Akka.Tests`). See `Akka.Tests/Serialization/V2PortKit` for a worked example.
