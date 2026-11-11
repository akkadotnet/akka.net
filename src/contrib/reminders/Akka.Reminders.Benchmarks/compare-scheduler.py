"""Run identical scheduler harnesses serially against the same dedicated PostgreSQL database.

Build each benchmark project in Release first. Set REMINDERS_BENCHMARK_CONNECTION_STRING
to a disposable benchmark database: the harness truncates reminders.scheduled_reminders.
"""
import argparse
import csv
import json
from pathlib import Path
import statistics
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--dev", type=Path, required=True)
parser.add_argument("--unfixed", type=Path, required=True)
parser.add_argument("--fixed", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--repeats", type=int, default=3)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
revisions = {name: getattr(args, name).resolve() for name in ("dev", "unfixed", "fixed")}
metadata = {
    name: subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=path, text=True).strip()
    for name, path in revisions.items()
}
(args.output / "revisions.json").write_text(json.dumps(metadata, indent=2) + "\n")
summary = []
with (args.output / "scheduler-results.jsonl").open("w") as stream:
    for count in (1000, 5000, 25000):
        for scenario in ("one-off", "due-recurring", "early-recurring", "recurring-retry"):
            for name, path in revisions.items():
                dll = path / "src/Akka.Reminders.Benchmarks/bin/Release/net9.0/Akka.Reminders.Benchmarks.dll"
                result = subprocess.run(
                    ["dotnet", str(dll), "--scheduler-throughput", scenario, str(count), str(args.repeats)],
                    cwd=path, capture_output=True, text=True, timeout=600)
                log = args.output / f"{name}-{scenario}-{count}.log"
                log.write_text(result.stdout + result.stderr)
                if result.returncode:
                    raise RuntimeError(f"{name} {scenario} {count} failed; see {log}")
                rows = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{"scenario"')]
                if len(rows) != args.repeats + 1 or any(row["deliveries"] != count for row in rows):
                    raise RuntimeError(f"Incomplete benchmark output: {log}")
                for row in rows:
                    row["revision"] = name
                    stream.write(json.dumps(row) + "\n")
                stream.flush()
                measured = [row for row in rows if not row["warmup"]]
                summary.append({
                    "count": count, "scenario": scenario, "revision": name,
                    "median_ms": round(statistics.median(row["elapsedMs"] for row in measured), 3),
                    "min_ms": round(min(row["elapsedMs"] for row in measured), 3),
                    "max_ms": round(max(row["elapsedMs"] for row in measured), 3),
                    "status_reads": measured[-1]["statusReads"],
                    "fetches": measured[-1]["fetches"], "commits": measured[-1]["commits"],
                    "overviews": measured[-1]["overviews"],
                })
                print(json.dumps(summary[-1]), flush=True)
                with (args.output / "scheduler-comparison.csv").open("w", newline="") as table:
                    writer = csv.DictWriter(table, fieldnames=list(summary[0]))
                    writer.writeheader()
                    writer.writerows(summary)
