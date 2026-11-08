# Miscellaneous-message native codec consumer

This ordinary public consumer is prepared to check all 46 message samples (including the explicit legacy-empty FromConfig canonical control) / 22 manifests through registered serializer56, with the existing global switch off/on and an explicit whole-protocol legacy pin. It uses public serializers and public RemoteRouterConfig surrogates; no private codec, friend identity, reflective bootstrap or blanket assembly roots.

The checks retain complete field assertions, registered native buffer writes, signed UID
extremes, full address metadata, nullable payloads, all pool options/resizer fields,
legacy-empty FromConfig canonicalization, native rich exception state and whole-output
atomicity when a registered native payload writes 8 KiB then fails. LocalScope retains its
codec but has no default binding.

The original published Misc source (`e3fea6a51`, PR #8934) excluded its two known non-null
legacy exception samples because reflective constructor metadata could be missing. The
original 132-control receipt below remains retained unchanged. This separate test-only
follow-up composes the reviewed fix from [#8935](https://github.com/akkadotnet/akka.net/pull/8935),
product commit `59dd249e2`, and requires all 138 legacy reads, including six exception reads.
Legacy comparisons retain old HResult, HelpLink and Data losses; V2 comparisons retain full state.

The retained Linux x64 receipt passed: 138 native cases,132 legacy control reads,135 selected-writer checks and45 rejection checks. A late native payload ran36 times and attempted294,912 bytes; each containing message retained empty caller output. Seven pre-existing Remote ILC warnings remain visible. The initial failed AR assertion receipt is retained: AR expectations are now canonicalized through the unchanged legacy reader and checked for canonical runtime type, full path/UID and public equality; other exact type/field assertions remain. The composed branch includes the SDK servicing prerequisite #8914. Publish with SDK 10.0.114/compiler-runtime 10.0.14:

```bash
DOTNET_ROOT=/path/to/sdk10.0.114 PATH=/path/to/sdk10.0.114:$PATH dotnet publish /path/to/akka.net/src/aot/Akka.Remote.MiscMessage.AOT.App/Akka.Remote.MiscMessage.AOT.App.csproj -c Release -r linux-x64 -o /tmp/misc-native
/tmp/misc-native/Akka.Remote.MiscMessage.AOT.App
```

The app asserts dynamic code is unavailable and dynamic type loading is explicitly false. ILC diagnostics remain visible; no new NoWarn is used. The original standalone proof added no warning baseline. This is not full remoting/native transport compatibility, arbitrary user-payload support, or a benchmark signoff.

The fixed-composition native run passed separately after the earlier standalone proof.
Its required completion marker is:

```text
[misc-native] FIXED COMPOSITION codec proof: native-cases=138 legacy-reads=138 legacy-exception-reads=6 selected-writer-checks=135 rejected=45 late-payload-calls=36 late-payload-bytes=294912
```

The fixed-composition native run passed with five existing Remote warning keys and zero new, outside-scope or source-less warnings. The original standalone result remains distinguished from the corrected composition.

## CI gate

`RemoteMiscMessageAotCanary` invokes the shared
[`native-codec-canary.template.yaml`](../../../build-system/native-codec-canary.template.yaml)
from the merged [#8935](https://github.com/akkadotnet/akka.net/pull/8935) dependency. The job
cold-publishes this ordinary consumer, requires native exit zero and the exact completion
marker above, checks Remote/V2 runtime/app/Shared warnings against the reviewed five-key
Remote debt baseline, and requires zero diagnostics without a source location. Full SDK,
publish, run and warning logs are retained even on failure. Outside-scope diagnostics are
reported and remain owned by the appropriate separate canaries. This gate does not establish
native Remote startup, network transport, arbitrary payload support or performance acceptance.
Hosted execution of the new Azure invocation remains pending.

The final local replay passed every native completion and warning assertion on SDK 10.0.114/runtime 10.0.14, with five known warnings and zero new, outside-scope or unlocated diagnostics. This validates the commands; hosted execution of the new job remains pending.
