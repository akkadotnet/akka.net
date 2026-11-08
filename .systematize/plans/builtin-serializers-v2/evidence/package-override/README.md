# Packaged AOT override proof

SDK 10.0.114/runtime 10.0.14, Linux x64. The complete reviewed script passed at source commit 8c4640e39, without edits during execution. It packed one unique candidate version and used that exact package in all three consumers outside repository build properties.

- Full automatic-default native consumer: exit 0 and `[canary] OK`; both original actor-system scenarios and the complete original router/stash/timer/FSM/death-watch/PipeTo/dispatcher/mailbox corpus remain unchanged.
- Full missing-buildTransitive control: publish succeeded, then the exact explicit-switch runtime assertion failed. A bounded direct recheck captured exit 1. Its existing visible diagnostic settings and all original source remain unchanged.
- Small late-user-override native consumer: native publish/run succeeded with warnings fatal. The post-package target asserted exactly one option with `Value=true` and `Trim=true`; `TryGetSwitch` required found=true/value=true, and actual native execution was required. Both exact success markers appear in retained logs. This is configuration precedence proof, not a claim of arbitrary reflection safety under Native AOT.

Slopwatch reports zero issues. All auditing and existing full-canary watchdog/settings remain intact; no warning baseline or source suppression was added. The deliberately missing-target publish retains its full reflection/Newtonsoft warning output. The new config-only publish has no warnings and keeps warnings fatal.

`generated-inputs-and-packages.tar.gz` preserves all three generated source/project/late-target files, restore assets and NuGet imports, the shared watchdog input, NuGet.Config, and the actual candidate nupkg/snupkg. `INPUT_SHA256SUMS` checks the extracted bytes. Absolute paths in generated inputs/logs preserve provenance; running the retained script from a checkout generates fresh portable paths for that checkout.

`prior-ci-green-checkpoint.json.gz` preserves the already-green CI state at 36a70e8e7 before this extension. The extension's local proof does not claim hosted CI at its new head. Full original and new publish/run logs, pack output, SDK, exits, exact commands and verified script bytes are retained without filtering. `SHA256SUMS` includes this local `.gitattributes`, whose `* -text` preserves raw evidence bytes across checkout. Direct references only; transitive-only references, other override locations, PublishTrimmed-only and rooted-library coverage remain outside this check.
