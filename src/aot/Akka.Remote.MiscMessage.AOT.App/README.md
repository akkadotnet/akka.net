# Miscellaneous-message native codec consumer

This ordinary public consumer is prepared to check all 46 message samples (including the explicit legacy-empty FromConfig canonical control) / 22 manifests through registered serializer56, with the existing global switch off/on and an explicit whole-protocol legacy pin. It uses public serializers and public RemoteRouterConfig surrogates; no private codec, friend identity, reflective bootstrap or blanket assembly roots.

The intended checks retain complete field assertions, registered native buffer writes, signed UID extremes, full address metadata, nullable payloads, all pool options/resizer fields, legacy-empty FromConfig canonicalization, native rich exception state and whole-output atomicity when a registered native payload writes8KiB then fails. LocalScope retains its codec but has no default binding. The unchanged legacy16 exception reader has a known reflective-constructor boundary under Native AOT; this consumer excludes legacy exception reads from supported coverage until its separate prerequisite lands.

The retained Linux x64 receipt passed: 138 native cases,132 legacy control reads,135 selected-writer checks and45 rejection checks. A late native payload ran36 times and attempted294,912 bytes; each containing message retained empty caller output. Seven pre-existing Remote ILC warnings remain visible. The initial failed AR assertion receipt is retained: AR expectations are now canonicalized through the unchanged legacy reader and checked for canonical runtime type, full path/UID and public equality; other exact type/field assertions remain. From outside the repository (so global.json does not select SDK10.0.101), publish with serviced SDK10.0.114/compiler-runtime10.0.14:

```bash
DOTNET_ROOT=/path/to/sdk10.0.114 PATH=/path/to/sdk10.0.114:$PATH dotnet publish /path/to/akka.net/src/aot/Akka.Remote.MiscMessage.AOT.App/Akka.Remote.MiscMessage.AOT.App.csproj -c Release -r linux-x64 -o /tmp/misc-native
/tmp/misc-native/Akka.Remote.MiscMessage.AOT.App
```

The app asserts dynamic code is unavailable and dynamic type loading is explicitly false. ILC diagnostics remain visible; no new NoWarn or warning baseline is used. This is not full remoting/native transport compatibility, arbitrary user-payload support, or a benchmark signoff.
