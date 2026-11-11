#!/usr/bin/env bash
# Compares the ILC trim/AOT warnings in a `dotnet publish` log with the canary's baseline.
#
#   check-aot-warnings.sh <publish.log> [baseline]   fail on new warnings
#   check-aot-warnings.sh <publish.log> --print      print the normalized warning list
#
# Fails when the log has a warning from an Akka.Reminders member, or any warning that is not in
# the baseline. Baseline entries that no longer appear are reported but do not fail the check.
set -euo pipefail

log="${1:?usage: check-aot-warnings.sh <publish.log> [baseline|--print]}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
baseline="${2:-$script_dir/Akka.Reminders.AOT.App/aot-warnings.baseline.txt}"

# "ILC : Trim analysis warning IL2057: Member: text [/path/to/project.csproj]" -> "IL2057: Member: text"
normalize() {
    grep -E 'warning IL[0-9]+' "$1" \
        | sed -E 's/ \[[^]]*\.csproj\]$//; s/^.*warning (IL[0-9]+): /\1: /' \
        | sort -u || true
}

actual="$(mktemp)"
expected="$(mktemp)"
trap 'rm -f "$actual" "$expected"' EXIT

normalize "$log" > "$actual"

if [[ "$baseline" == "--print" ]]; then
    cat "$actual"
    exit 0
fi

grep -vE '^\s*(#|$)' "$baseline" | sort -u > "$expected"

status=0

reminders="$(grep -E '^IL[0-9]+: Akka\.Reminders' "$actual" || true)"
if [[ -n "$reminders" ]]; then
    echo "::error::Trim/AOT warnings from Akka.Reminders code:"
    echo "$reminders"
    status=1
fi

new="$(comm -23 "$actual" "$expected")"
if [[ -n "$new" ]]; then
    echo "::error::New trim/AOT warnings not in $(basename "$baseline"):"
    echo "$new"
    status=1
fi

gone="$(comm -13 "$actual" "$expected")"
if [[ -n "$gone" ]]; then
    echo "Baseline warnings no longer reported (consider removing them):"
    echo "$gone"
fi

echo "AOT warnings: $(wc -l < "$actual") reported, $(wc -l < "$expected") in baseline."
exit $status
