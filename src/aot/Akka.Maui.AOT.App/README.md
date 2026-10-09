# Akka.Maui.AOT.App

Native AOT canary for Akka.NET inside a real .NET MAUI app. It is hosted the way a MAUI app should host
Akka.NET: `MauiApp.CreateBuilder()` plus `AddAkkaMaui(...)` from `Akka.Hosting.Maui`.

The screen shows live metrics. `TickerActor` runs an Akka timer and sends each `Sample` to `MetricsActor`
(the actor-to-actor hop). `MetricsActor` aggregates the samples and posts a snapshot to `MetricsViewModel`,
which applies it on the UI thread. Three controls are bound to the view model with compiled (lambda)
bindings, which Native AOT requires.

## What it proves

Against an unrooted `PublishAot` publish:

* `AddAkkaMaui` starts the `ActorSystem` inside `MauiAppBuilder.Build()`, so both actors are registered
  before MAUI creates the first page. MAUI never starts `IHostedService` instances (dotnet/maui#2244),
  which is why plain `AddAkka` cannot be used here.
* Plain `AddAkka` still throws `PlatformNotSupportedException` inside the app. This checks that Akka.Hosting's
  MAUI detection sees MAUI under Native AOT, with `InvariantGlobalization` on and a satellite resource
  assembly (`Resources/Strings.cs.resx`) in the app. That combination crashed the detection before #8783.
* The actors drive exactly 25 updates to the screen. The self-test counts changes to the bound label's
  `Text` and checks they are `Samples: 1` .. `Samples: 25`, in order, all on the UI thread, with no extras.
  It then asks `MetricsActor` how many samples it saw.
* No Warning or Error is logged anywhere. `CanaryLoggerProvider` is the app's only logging provider, so it
  sees Akka's log events (through `LoggerFactoryLogger`) and MAUI's own, including binding failures.
* `CoordinatedShutdown` terminates the `ActorSystem` cleanly before the app quits through
  `Application.Quit()`.
* `Akka.DynamicTypeLoading` is off, so everything Akka.NET needs is registered in code.

## Targets

| Target | Native AOT | Built in CI | Run in CI |
| --- | --- | --- | --- |
| Mac Catalyst | supported | yes (`macos-latest`) | yes |
| Android | experimental in .NET 10 (`XA1040`), needs NDK r27+ | no | no (needs an emulator) |
| iOS | supported | no | no |
| Windows | not supported by MAUI yet (dotnet/maui#31227) | no | no |

The project targets `net10.0-maccatalyst26.0` on macOS and `net10.0-android` everywhere else, so a restore never
needs a workload the machine cannot install. It is not in `Akka.slnx` because the solution build runs on agents
without the MAUI workloads.

## Run

### Mac Catalyst (macOS, Xcode 26.0)

The target is pinned to Mac Catalyst 26.0, and that pack builds only with Xcode 26.0.x
(`sudo xcode-select -s /Applications/Xcode_26.0.app/Contents/Developer`). To build with another Xcode, pass
the matching version: `-p:TargetFrameworks=net10.0-maccatalyst<version> -f net10.0-maccatalyst<version>`.

```bash
dotnet workload install maui-maccatalyst
rm -rf src/aot/Akka.Maui.AOT.App/bin src/aot/Akka.Maui.AOT.App/obj
dotnet publish src/aot/Akka.Maui.AOT.App -f net10.0-maccatalyst26.0 -r maccatalyst-arm64 -c Release
# the bundle is named after ApplicationTitle ("Akka.NET MAUI canary.app"); the executable keeps the assembly name
APP=$(find src/aot/Akka.Maui.AOT.App/bin -name '*.app' -type d -prune | head -1)

# interactive: the window shows the metrics ticking
open "$APP"

# self-test: drives the updates, checks them, prints the OK line and exits
AKKA_CANARY_SELFTEST=1 "$APP/Contents/MacOS/Akka.Maui.AOT.App"
```

Use `-r maccatalyst-x64` on an Intel Mac.

### Android (any OS)

```bash
dotnet workload install maui-android
# installs the Android SDK and a JDK if you do not have them; Native AOT also needs the NDK (r27 or later)
dotnet build src/aot/Akka.Maui.AOT.App -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory=<sdk dir> -p:JavaSdkDirectory=<jdk dir> -p:AcceptAndroidSdkLicenses=True
dotnet publish src/aot/Akka.Maui.AOT.App -f net10.0-android -r android-arm64 -c Release \
  -p:AndroidSdkDirectory=<sdk dir> -p:JavaSdkDirectory=<jdk dir> -p:AndroidNdkDirectory=<ndk dir>

adb install -r src/aot/Akka.Maui.AOT.App/bin/Release/net10.0-android/android-arm64/publish/net.getakka.mauicanary-Signed.apk
adb shell am start -n net.getakka.mauicanary/crc64<hash>.MainActivity --ez selftest true
adb logcat | grep canary-maui
```

`adb shell cmd package resolve-activity --brief net.getakka.mauicanary` prints the activity name.

## Self-test mode

On when the `AKKA_CANARY_SELFTEST` environment variable is `1`, the app gets a `--selftest` argument, or (Android)
the launch intent has the `selftest` extra. Pass condition: exit code `0` and `[canary-maui] OK` on stdout. A
failed check prints `[canary-maui] FAILED: ...` and exits `1`; the run gives up after 3 minutes with exit code `2`.

## Warning baseline

`aot-warnings.baseline.txt` tracks `IL2xxx`/`IL3xxx` warnings from all Akka.NET code (`src/core/`,
`src/contrib/`) that this unrooted MAUI publish reaches. It is empty. The CI step also passes
`--unlocated-scope Akka.` to `scripts/CheckAotWarnings.cs`, because a MAUI publish can hand ILC assemblies
without their PDBs (Android always rewrites them first). Those warnings have no source file and cannot be
baselined, so any of them from an `Akka.*` member fails the check.
