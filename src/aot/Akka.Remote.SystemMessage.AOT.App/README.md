# System-message native codec consumer

This ordinary public consumer checks the registered serializer 62 across the complete 18-sample / ten-manifest corpus, global off/on and explicit legacy pin writer selection, native buffer writes, all four unsupported nested exception families and NoMessage rejection before caller output. It uses no private codec, friend identity, reflective bootstrap or blanket assembly roots. The public legacy serializer constructor anchors Akka.Remote; an existing public fallback-alias overload resolves the read-only row using an otherwise unbound consumer marker.

The original published System source (`9ffe2853b`, PR #8932) had a legacy id 22 boundary:
reflective constructor metadata could be missing, and the initial native probe lost a known
Create cause. Its original receipt counted only 42 controls and remains retained unchanged.

This separate test-only follow-up composes the reviewed fix from
[#8935](https://github.com/akkadotnet/akka.net/pull/8935), product commit `59dd249e2`.
It checks all 54 legacy reads, including 12 known non-null exception causes, and every selected
writer read-back. Legacy comparisons retain old HResult, HelpLink and Data losses; they do
not demand G8's richer state. Full remoting, native transport and arbitrary user payloads
remain outside this bounded consumer.

The composed branch includes the SDK servicing prerequisite #8914. Publish using SDK 10.0.114/compiler-runtime 10.0.14:

```bash
DOTNET_ROOT=/path/to/sdk10.0.114 PATH=/path/to/sdk10.0.114:$PATH dotnet publish /path/to/akka.net/src/aot/Akka.Remote.SystemMessage.AOT.App/Akka.Remote.SystemMessage.AOT.App.csproj -c Release -r linux-x64 -o /tmp/system-native
/tmp/system-native/Akka.Remote.SystemMessage.AOT.App
```

The retained Linux x64 receipt passed with 54 registered-id62 cases (18 samples × three modes), 42 legacy22 control reads, 54 selected-writer checks and 18 zero-output rejection checks. The publish receipt retains seven pre-existing Remote diagnostics, including the unsupported legacy exception reader. The app asserts that dynamic code is unavailable and the dynamic type loading switch is explicitly false. ILC warnings stay visible (matching existing native canaries); this is not a warning-free Remote audit. The original standalone proof added no NoWarn, warning baseline or global public setting.

The fixed-composition receipt is retained separately in `evidence/with-legacy-exception-fix-*`.
Its required completion marker is:

```text
[system-native] FIXED COMPOSITION codec proof: native-cases=54 legacy-reads=54 legacy-exception-reads=12 selected-writer-checks=54 rejected=18 dynamic-code=false
```

The fixed-composition native run passed with five existing Remote warning keys and zero new,
outside-scope or source-less warnings. Exact commands and compressed full receipts are retained
next to the original evidence; no old receipt was overwritten.

## CI gate

`RemoteSystemMessageAotCanary` invokes the shared
[`native-codec-canary.template.yaml`](../../../build-system/native-codec-canary.template.yaml)
from the merged [#8935](https://github.com/akkadotnet/akka.net/pull/8935) dependency. The job
cold-publishes this ordinary consumer, requires native exit zero and the exact completion
marker above, checks Remote/V2 runtime/app/Shared warnings against the reviewed five-key
Remote debt baseline, and requires zero diagnostics without a source location. Full SDK,
publish, run and warning logs are retained even on failure. Outside-scope diagnostics are
reported and remain owned by the appropriate separate canaries. This gate does not establish
native Remote startup, network transport, arbitrary payload support or performance acceptance.
Hosted execution of the new Azure invocation remains pending.

The `evidence/ci-template-replay` directory retains the exact expanded Bash, parameters,
template source, invocation and source hashes, plus compressed full SDK/publish/run/warning
logs. The final local replay passed every native completion and warning assertion on SDK
10.0.114/runtime 10.0.14, with five known warnings and zero new, outside-scope or unlocated
diagnostics. This validates the commands; it does not claim hosted execution of the new job.
