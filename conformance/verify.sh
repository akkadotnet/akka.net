#!/usr/bin/env bash
#
# verify.sh — build and hand-verify the ACT (Akka Conformance Tester) system end to end.
#
# Builds the C# reference seed (act-host, which embeds the modified Akka.Cluster) and the Go worker,
# then drives the C#, Go, and JavaScript workers — each against its own fresh reference seed — through
# the full 10-step conformance ladder and prints a PASS/FAIL summary.
#
# Portable to macOS (incl. Apple Silicon / M3) and Linux: POSIX tools only, no `timeout`, bash 3.2 OK.
# Everything runs on 127.0.0.1 (loopback), so no firewall prompts and no networking setup.

set -u

# --- locate repo root (this script lives in <root>/conformance) ---
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ACTHOST_PROJ="$ROOT/conformance/act-host/ActHost.csproj"
ACTHOST_DLL="$ROOT/conformance/act-host/bin/Release/net10.0/act-host.dll"

HOST_SECONDS="${HOST_SECONDS:-50}"   # max lifetime of each reference-seed run (exits early once worker leaves)
WORK="$(mktemp -d 2>/dev/null || echo "/tmp/act-verify.$$")"
mkdir -p "$WORK"

PIDS=""
cleanup() { for p in $PIDS; do kill "$p" 2>/dev/null || true; done; }
trap cleanup EXIT INT TERM

say()  { printf '\n\033[1m== %s ==\033[0m\n' "$*"; }
ok()   { printf '\033[32m%s\033[0m\n' "$*"; }
bad()  { printf '\033[31m%s\033[0m\n' "$*"; }

# --- preflight: required toolchains ---
need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    bad "MISSING: '$1' not found on PATH."
    echo "  Install on macOS with: brew install $2"
    return 1
  fi
  return 0
}

say "Preflight"
missing=0
need dotnet  dotnet || missing=1
need go      go     || missing=1
need node    node   || missing=1
need python3 python || missing=1
if [ "$missing" -ne 0 ]; then
  bad "Install the missing toolchain(s) above, then re-run. (macOS: \`brew install dotnet go node python\`)"
  exit 1
fi
echo "dotnet $(dotnet --version) | $(go version) | node $(node --version) | $(python3 --version 2>&1)"

# --- build ---
say "Building reference seed (act-host) — first build also compiles the modified Akka.Cluster"
if ! dotnet build "$ACTHOST_PROJ" -c Release >"$WORK/build-acthost.log" 2>&1; then
  bad "act-host build FAILED — see $WORK/build-acthost.log"; tail -25 "$WORK/build-acthost.log"; exit 1
fi
ok "act-host built."

say "Building Go worker"
if ! ( cd "$ROOT/conformance/go-worker" && go build -o go-worker . ) >"$WORK/build-go.log" 2>&1; then
  bad "go build FAILED — see $WORK/build-go.log"; tail -25 "$WORK/build-go.log"; exit 1
fi
ok "go-worker built."

# Runs one external worker against a fresh reference seed and reports the ACT verdict.
#   run_worker <name> <seed-port> <worker-port> <go|js> [extra worker args...]
run_worker() {
  name="$1"; seed_port="$2"; work_port="$3"; kind="$4"; shift 4
  hlog="$WORK/$name-host.log"; wlog="$WORK/$name-worker.log"

  dotnet "$ACTHOST_DLL" --port="$seed_port" --seconds="$HOST_SECONDS" >"$hlog" 2>&1 &
  host_pid=$!; PIDS="$PIDS $host_pid"

  # wait for the seed to be ready and learn its URI
  i=0
  while [ "$i" -lt 120 ]; do grep -q ACT_HOST_READY "$hlog" 2>/dev/null && break; sleep 0.5; i=$((i+1)); done
  seed_uri="$(grep '^SEED_URI=' "$hlog" 2>/dev/null | head -1 | cut -d= -f2-)"
  if [ -z "$seed_uri" ]; then bad "[$name] reference seed failed to start"; tail -15 "$hlog"; return 1; fi
  echo "[$name] seed = $seed_uri ; worker port = $work_port"

  case "$kind" in
    go) ( cd "$ROOT/conformance/go-worker" && ./go-worker --seed="$seed_uri" --port="$work_port" "$@" ) >"$wlog" 2>&1 ;;
    js) ( cd "$ROOT/conformance/js-worker" && node worker.js --seed="$seed_uri" --port="$work_port" "$@" ) >"$wlog" 2>&1 ;;
    py) ( cd "$ROOT/conformance/py-worker" && python3 worker.py --seed="$seed_uri" --port="$work_port" "$@" ) >"$wlog" 2>&1 ;;
  esac

  wait "$host_pid" 2>/dev/null   # the seed self-terminates once the worker has left (or after HOST_SECONDS)

  verdict="$(grep -E 'CONFORMANCE (PASSED|FAILED)' "$hlog" | head -1)"
  echo "$verdict" | grep -q PASSED && ok "[$name] $verdict" || { bad "[$name] ${verdict:-no verdict (see $hlog / $wlog)}"; return 1; }
}

# --- C# in-process worker via the conformance test suite (positive + negative) ---
say "C# in-process worker — running the conformance test suite"
cs_status="PASS"
if dotnet test "$ROOT/src/contrib/cluster/Akka.Cluster.Conformance.Tests/Akka.Cluster.Conformance.Tests.csproj" \
     -c Release --framework net10.0 >"$WORK/cs-tests.log" 2>&1; then
  ok "[csharp] $(grep -E '^(Passed!|Failed!)' "$WORK/cs-tests.log" | tail -1)"
else
  bad "[csharp] tests FAILED — see $WORK/cs-tests.log"; cs_status="FAIL"
fi

# --- Go and JS workers against fresh reference seeds ---
say "Go worker — full 10-step ladder"
go_status="PASS"; run_worker go 5210 6200 go --run=30 || go_status="FAIL"

say "JavaScript worker — full 10-step ladder"
js_status="PASS"; run_worker js 5211 6201 js --run=30 || js_status="FAIL"

say "Python worker — full 10-step ladder"
py_status="PASS"; run_worker py 5212 6202 py --run=30 || py_status="FAIL"

# --- summary ---
say "Summary"
printf '  C# (in-process) : %s\n' "$cs_status"
printf '  Go              : %s\n' "$go_status"
printf '  JavaScript      : %s\n' "$js_status"
printf '  Python          : %s\n' "$py_status"
echo "  logs: $WORK"
if [ "$cs_status" = PASS ] && [ "$go_status" = PASS ] && [ "$js_status" = PASS ] && [ "$py_status" = PASS ]; then
  ok "ALL WORKERS PASSED the 10-step ACT conformance ladder."
  exit 0
fi
bad "One or more workers did not pass — inspect the logs above."
exit 1
