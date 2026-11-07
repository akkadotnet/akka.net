# System-message native codec consumer

This ordinary public consumer checks the registered serializer 62 across the complete 18-sample / ten-manifest corpus, global off/on and explicit legacy pin writer selection, native buffer writes, all four unsupported nested exception families and NoMessage rejection before caller output. It uses no private codec, friend identity, reflective bootstrap or blanket assembly roots. The public legacy serializer constructor anchors Akka.Remote; an existing public fallback-alias overload resolves the read-only row using an otherwise unbound consumer marker.

The unchanged legacy22 exception reader has a known native boundary: its reflective serialization constructor metadata can be missing and a known Create failure was lost in the initial native probe. This app explicitly checks legacy22 controls only; it does not count legacy exception reads as supported. Full remoting, native transport and arbitrary user payloads are outside this bounded consumer.

From outside the repository (so global.json does not select SDK10.0.101), use serviced SDK10.0.114/compiler-runtime10.0.14:

```bash
DOTNET_ROOT=/path/to/sdk10.0.114 PATH=/path/to/sdk10.0.114:$PATH dotnet publish /path/to/akka.net/src/aot/Akka.Remote.SystemMessage.AOT.App/Akka.Remote.SystemMessage.AOT.App.csproj -c Release -r linux-x64 -o /tmp/system-native
/tmp/system-native/Akka.Remote.SystemMessage.AOT.App
```

The retained Linux x64 receipt passed with 54 registered-id62 cases (18 samples × three modes), 42 legacy22 control reads, 54 selected-writer checks and 18 zero-output rejection checks. The publish receipt retains seven pre-existing Remote diagnostics, including the unsupported legacy exception reader. The app asserts that dynamic code is unavailable and the dynamic type loading switch is explicitly false. ILC warnings stay visible (matching existing native canaries); this is not a warning-free Remote audit. No new NoWarn, warning baseline or global public setting is used.
