#!/usr/bin/env python3
"""Repeat the real-payload RemotePingPong workload with rotated, sequential arms.

Each process executes one same-sized warmup and one measured repetition. Historical
regression mode uses the unchanged old CLI; transport mode uses the current CLI.
"""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import time


def git(directory, *args):
    return subprocess.check_output(["git", "-C", str(directory), *args], text=True).strip()


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", choices=["regression", "transports"], required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--historical-root", type=Path)
    parser.add_argument("--adapter-revision", help="Optional initial-adapter commit to compare alongside pre-/post-TLS snapshots")
    parser.add_argument("--binary", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--pattern", choices=["ping-pong", "oneway"], required=True)
    parser.add_argument("--messages", type=int, required=True)
    parser.add_argument("--clients", type=int, default=16)
    parser.add_argument("--window", type=int, default=50)
    parser.add_argument("--lanes", type=int, nargs="+", default=[1, 2, 4, 8])
    parser.add_argument("--quic-stream-counts", type=int, nargs="+",
                        help="Vary native streams separately; each count must divide every selected lane count")
    parser.add_argument("--queue-budget", type=int, default=8192)
    parser.add_argument("--repetitions", type=int, default=5)
    parser.add_argument("--round-offset", type=int, default=0, help="Preserve rotation when collecting additional rounds")
    parser.add_argument("--keep-going", action="store_true", help="Retain invalid attempts and finish the matrix; exit remains nonzero")
    parser.add_argument("--minimum-seconds", type=float, default=3)
    parser.add_argument("--timeout", type=float, default=180)
    result = parser.parse_args()
    if min(result.messages, result.clients, result.window, result.repetitions, *result.lanes) <= 0:
        parser.error("message/client/window/repetition/lane counts must be positive")
    if result.queue_budget < max(result.lanes) or result.minimum_seconds <= 0 or result.timeout <= 0:
        parser.error("invalid queue budget or timing bounds")
    if result.phase == "regression" and result.historical_root is None:
        parser.error("regression requires --historical-root (pre-tls/post-tls checkouts)")
    if result.phase == "transports" and result.binary is None:
        parser.error("transports requires --binary")
    if result.adapter_revision is not None:
        if result.phase != "regression" or re.fullmatch(r"[0-9a-fA-F]{7,40}", result.adapter_revision) is None:
            parser.error("--adapter-revision requires regression mode and a 7–40 digit commit hash")
    if result.quic_stream_counts is not None:
        if result.phase != "transports":
            parser.error("--quic-stream-counts requires --phase transports")
        if any(stream < 1 or stream > 64 or lanes % stream for stream in result.quic_stream_counts for lanes in result.lanes):
            parser.error("QUIC stream counts must be 1–64 and divide every selected lane count to match receive-path budgets")
    if len(set(result.lanes)) != len(result.lanes) or result.round_offset < 0:
        parser.error("lane counts must be unique and round offset must be nonnegative")
    if result.quic_stream_counts and len(set(result.quic_stream_counts)) != len(result.quic_stream_counts):
        parser.error("QUIC stream counts must be unique")
    if any(result.queue_budget % lanes for lanes in result.lanes):
        parser.error("queue budget must divide evenly across selected lanes")
    if result.output.exists():
        parser.error("output directory already exists; keep earlier evidence and choose another path")
    return result


def cells(args):
    groups = []
    if args.phase == "regression":
        snapshots = []
        revisions = [("pre-tls", "a6cbc8a6e"), ("post-tls", "2d6a06797")]
        if args.adapter_revision is not None:
            revisions.append(("adapter", args.adapter_revision.lower()))
        for name, expected in revisions:
            checkout = args.historical_root.resolve() / name
            revision = git(checkout, "rev-parse", "HEAD")
            if not revision.startswith(expected):
                raise ValueError(f"{name} is at unexpected revision {revision}")
            tree = git(checkout, "rev-parse", "HEAD:src/benchmark/RemotePingPong")
            if tree != "074976e4da8036fdb9d0a598484e33b5e47e696d":
                raise ValueError(f"historical harness differs: {name}")
            binary = checkout / "src/benchmark/RemotePingPong/bin/Release/net10.0/RemotePingPong.dll"
            snapshots.append((name, revision, binary))
        groups.append([dict(name=name + "-dotnetty", revision=rev, binary=str(binary),
                            transport="dotnetty", lanes=1, streams=None, flags=[])
                       for name, rev, binary in snapshots])
        for lanes in args.lanes:
            groups.append([dict(name=f"{name}-artery-l{lanes}", revision=rev, binary=str(binary),
                                transport="artery-tcp", lanes=lanes, streams=1, flags=["--artery"])
                           for name, rev, binary in snapshots])
    else:
        binary = str(args.binary.resolve())
        repository = Path(__file__).resolve().parents[3]
        revision = git(repository, "rev-parse", "HEAD")
        groups.append([dict(name="dotnetty", revision=revision, binary=binary,
                            transport="dotnetty", lanes=1, streams=None, flags=["--transport", "dotnetty"])])
        for lanes in args.lanes:
            group = []
            for transport in ["artery-tcp", "artery-tls", "quic"]:
                stream_counts = (args.quic_stream_counts or [lanes]) if transport == "quic" else [1]
                for streams in stream_counts:
                    suffix = f"-s{streams}" if transport == "quic" and args.quic_stream_counts is not None else ""
                    group.append(dict(name=f"{transport}-l{lanes}{suffix}", revision=revision, binary=binary,
                                      transport=transport, lanes=lanes, streams=streams,
                                      flags=["--transport", transport]))
            groups.append(group)
    for group in groups:
        for cell in group:
            if not Path(cell["binary"]).is_file():
                raise FileNotFoundError(cell["binary"])
    return groups


def command(args, cell):
    result = [args.dotnet, cell["binary"], "run", *cell["flags"], "--payload", "real", "--serializer", "v2",
              "--clients", str(args.clients), "--window", str(args.window), "--msgs", str(args.messages), "--times", "2"]
    if args.pattern == "oneway":
        result.append("--oneway")
    if cell["transport"] != "dotnetty":
        # Match total receive paths: S streams each have N/S processing lanes. At S=N
        # each stream uses its inline processor, avoiding accidental N*N consumers.
        inbound = cell["lanes"] // cell["streams"] if cell["transport"] == "quic" else cell["lanes"]
        result += ["--inbound-lanes", str(inbound), "--outbound-lanes", str(cell["lanes"]),
                   "--qsize", str(args.queue_budget // cell["lanes"])]
    if cell["transport"] == "quic":
        result += ["--quic-streams", str(cell["streams"])]
    return result


def parse_run(output, clients, cell, phase):
    data = []
    for line in output.splitlines():
        if re.match(rf"^\s*{clients},", line):
            values = [float(value.strip()) for value in line.split(",")]
            if len(values) != 6:
                raise ValueError("unexpected benchmark row")
            data.append(dict(clients=int(values[0]), messages=int(values[1]), messages_per_second=values[2],
                             milliseconds=values[3], start_threads=int(values[4]), end_threads=int(values[5])))
    if len(data) != 2:
        raise ValueError(f"expected warmup + measurement rows, got {len(data)}")
    if "Round-trip check:                  PASSED" not in output:
        raise ValueError("serializer round-trip proof missing")
    if "[ERROR]" in output or "SUSPECT" in output or "WARNING:" in output:
        raise ValueError("error, dropped message or application resend invalidates row")
    size = re.search(r"Serialized payload size:\s+(\d+) bytes", output)
    if size is None or int(size.group(1)) != 348:
        raise ValueError("unexpected real V2 payload size")
    if phase == "transports" and cell["transport"] != "dotnetty":
        for direction in ["send", "reply"]:
            matches = re.findall(rf"Recipient lane occupancy \({direction}\):\s+([0-9,]+)", output)
            if len(matches) != 2:
                raise ValueError(f"missing {direction} recipient lane occupancy")
            for row, counts in zip(data, matches):
                counts = [int(value) for value in counts.split(",")]
                if len(counts) != cell["lanes"] or min(counts) <= 0 or sum(counts) != clients:
                    raise ValueError(f"{direction} workload did not occupy every configured lane")
                row[direction + "_lane_recipients"] = counts
        if cell["transport"] == "quic":
            opened = re.findall(r"QUIC ordinary streams open:\s+(\d+)", output)
            if opened != [str(cell["streams"])] * 2:
                raise ValueError("actual native ordinary stream count differs from configured count")
            budgets = re.findall(r"per-stream inbound lanes (\d+); receive paths (\d+)", output)
            expected = (str(cell["lanes"] // cell["streams"]), str(cell["lanes"]))
            if budgets != [expected] * 2:
                raise ValueError("effective QUIC receive-path budget differs from configured comparison")
    return data


def summary(output, records):
    groups = {}
    for record in records:
        if record["status"] == "valid":
            groups.setdefault(record["cell"], []).append(record["measured"]["messages_per_second"])
    with (output / "summary.csv").open("w", newline="") as file:
        writer = csv.writer(file)
        writer.writerow(["cell", "samples", "median_messages_sec", "min_messages_sec", "max_messages_sec", "stdev_percent"])
        for name, values in sorted(groups.items()):
            median = statistics.median(values)
            deviation = statistics.stdev(values) / statistics.mean(values) * 100 if len(values) > 1 else 0
            writer.writerow([name, len(values), round(median), round(min(values)), round(max(values)), round(deviation, 2)])


def main():
    args = arguments()
    groups = cells(args)
    repository = Path(__file__).resolve().parents[3]
    source_diff = git(repository, "diff", "HEAD", "--", "src/core", "src/benchmark/RemotePingPong")
    args.output.mkdir(parents=True)
    metadata = dict(arguments={key: str(value) if isinstance(value, Path) else value for key, value in vars(args).items()},
                    driver_revision=git(repository, "rev-parse", "HEAD"),
                    driver_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    source_diff_sha256=hashlib.sha256(source_diff.encode()).hexdigest() if source_diff else None,
                    system=platform.platform(), machine=platform.machine(), logical_cpus=os.cpu_count(),
                    affinity=sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None,
                    started_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                    dotnet_info=subprocess.check_output([args.dotnet, "--info"], text=True),
                    cells=groups, interpretation="loopback throughput only; one same-sized warmup before every measured run")
    for group in groups:
        for cell in group:
            binary = Path(cell["binary"])
            cell["binary_sha256"] = hashlib.sha256(binary.read_bytes()).hexdigest()
            cell["akka_dependencies_sha256"] = {
                dependency.name: hashlib.sha256(dependency.read_bytes()).hexdigest()
                for dependency in sorted(binary.parent.glob("Akka*.dll"))}
    (args.output / "manifest.json").write_text(json.dumps(metadata, indent=2))
    records = []
    invalid = False
    for repetition in range(args.round_offset, args.round_offset + args.repetitions):
        for group_index, group in enumerate(groups):
            offset = (repetition + group_index) % len(group)
            for cell in group[offset:] + group[:offset]:
                log_name = f"r{repetition + 1}-{cell['name']}.log"
                cmd = command(args, cell)
                record = dict(cell=cell["name"], revision=cell["revision"], repetition=repetition + 1,
                              transport=cell["transport"], lanes=cell["lanes"], ordinary_streams=cell["streams"],
                              inbound_lanes_per_stream=(cell["lanes"] // cell["streams"]
                                                        if cell["transport"] == "quic" else cell["lanes"]),
                              command=cmd, log=log_name, status="invalid")
                try:
                    process = subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=args.timeout)
                    (args.output / log_name).write_text(process.stdout)
                    record["exit"] = process.returncode
                    if process.returncode:
                        raise ValueError(f"process exited {process.returncode}")
                    warmup, measured = parse_run(process.stdout, args.clients, cell, args.phase)
                    expected = args.clients * args.messages * (1 if args.pattern == "oneway" else 2)
                    if measured["messages"] != expected:
                        raise ValueError("completion count differs from workload")
                    record.update(warmup=warmup, measured=measured)
                    if measured["milliseconds"] < args.minimum_seconds * 1000:
                        raise ValueError("measurement too short; keep evidence and rerun the paired matrix with more messages")
                    record["status"] = "valid"
                except subprocess.TimeoutExpired as error:
                    data = error.stdout or ""
                    (args.output / log_name).write_text(data.decode(errors="replace") if isinstance(data, bytes) else data)
                    record.update(status="timeout", error="process timeout")
                except ValueError as error:
                    record["error"] = str(error)
                records.append(record)
                with (args.output / "runs.jsonl").open("a") as file:
                    file.write(json.dumps(record) + "\n")
                summary(args.output, records)
                print(json.dumps({key: record.get(key) for key in ["cell", "repetition", "status", "measured", "error"]}), flush=True)
                if record["status"] != "valid":
                    invalid = True
                    if not args.keep_going:
                        return 1
    return 1 if invalid else 0


if __name__ == "__main__":
    raise SystemExit(main())
