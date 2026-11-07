# Built-in Serialization V2 native canary

Incremental proof for [#8718](https://github.com/akkadotnet/akka.net/issues/8718). The initial
corpus is the four Cluster Singleton hand-over messages, legacy id 14 and V2 id 54. A local
ActorSystem resolves both ids from the real module table with dynamic type loading disabled.
The canary checks all manifests with the global write switch off, on, and on with a legacy
protocol pin. It preserves domain singleton identity, exercises the native buffer contract,
rejects unknown manifests/ids, and requires exact case counts before reporting success.

This app is separate from the plain-core canary so adding a module does not change the
core-only boot and warning surface. It reuses `Shared/LogWatchdogFilter.cs`; runtime warnings
or errors fail the run. Later ports extend this corpus with actual round trips, including
generic payload reconstruction where applicable. Registering an id alone is not native proof.

## Run

Use the serviced SDK from `global.json` after the SDK update in #8914 (the local proof used
SDK 10.0.114 / runtime 10.0.14). Keep audit and warning checks enabled. Passing
`-p:PublishAot=true` globally would also change the library builds; the app enables it locally
when a runtime identifier is supplied.

```bash
set -o pipefail
rm -rf src/aot/Akka.Serialization.V2.AOT.App/bin src/aot/Akka.Serialization.V2.AOT.App/obj
dotnet publish src/aot/Akka.Serialization.V2.AOT.App -r linux-x64 -c Release \
  -o /tmp/v2-aot-publish 2>&1 | tee /tmp/v2-aot-publish.log
timeout 300 /tmp/v2-aot-publish/Akka.Serialization.V2.AOT.App
```

The run must exit 0 and print `[canary-v2] OK cases=12 native-writes=4 rejected=9`.
The runtime guard deliberately rejects managed execution; ordinary solution builds still
compile this app without native publishing.

## Warning and coverage boundaries

The cold-publish warning baseline covers Remote, Cluster, Cluster.Tools and this app. Existing
legacy reflection warnings remain visible and are tracked individually; the Singleton port
adds none. Core's separate rooted canary owns the Akka.dll baseline. Dependencies without source
locations are counted separately by the checker and are not approved by this baseline.

```bash
dotnet run scripts/CheckAotWarnings.cs -- \
  --log /tmp/v2-aot-publish.log \
  --baseline src/aot/Akka.Serialization.V2.AOT.App/aot-warnings.baseline.txt \
  --scope "src/core/Akka.Remote/,src/core/Akka.Cluster/,src/contrib/cluster/Akka.Cluster.Tools/,src/aot/Akka.Serialization.V2.AOT.App/" \
  --repo-root .
```

CI retains the full publish/run logs and checks both the exit status and completion marker.
Passing this corpus establishes native Singleton serialization, not Remote/Cluster startup,
network interoperability, singleton ownership hand-over, or arbitrary generic payload support.
