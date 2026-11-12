#!/usr/bin/env python3
"""Repeat the real-payload RemotePingPong workload with rotated, sequential arms.

Each process executes one same-sized warmup and one measured repetition. Historical
regression mode uses the unchanged old CLI; transport mode uses the current CLI.
"""

import argparse
import csv
from decimal import Decimal, InvalidOperation
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import time
import uuid


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
    parser.add_argument("--validate-sequence", action="store_true",
                        help="Use the ordered real V2 protocol and require final delivery proofs (transports phase only)")
    parser.add_argument("--rtt-interval-ms", type=int,
                        help="Fixed scheduled RTT offers; requires validated transports ping-pong and at most 1000000 samples")
    result = parser.parse_args()
    if result.validate_sequence and (result.phase != "transports" or result.messages > 1_000_000):
        parser.error("--validate-sequence requires transports phase and at most 1000000 messages per client")
    if result.rtt_interval_ms is not None and (not result.validate_sequence or result.pattern != "ping-pong"
            or not 1 <= result.rtt_interval_ms <= 1000 or result.messages < 2
            or result.messages * result.clients > 1_000_000):
        parser.error("paced RTT requires validated transports ping-pong, interval1–1000ms, at least2bursts and at most1000000samples")
    if min(result.messages, result.clients, result.window, result.repetitions, *result.lanes) <= 0:
        parser.error("message/client/window/repetition/lane counts must be positive")
    if result.queue_budget < max(result.lanes) or not math.isfinite(result.minimum_seconds) or not math.isfinite(result.timeout) \
            or result.minimum_seconds <= 0 or result.timeout <= 0:
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
    if args.validate_sequence:
        result.append("--validate-sequence")
    if getattr(args, "rtt_interval_ms", None) is not None:
        result += ["--rtt-interval-ms", str(args.rtt_interval_ms)]
    if cell["transport"] != "dotnetty":
        # Match total receive paths: S streams each have N/S processing lanes. At S=N
        # each stream uses its inline processor, avoiding accidental N*N consumers.
        inbound = cell["lanes"] // cell["streams"] if cell["transport"] == "quic" else cell["lanes"]
        result += ["--inbound-lanes", str(inbound), "--outbound-lanes", str(cell["lanes"]),
                   "--qsize", str(args.queue_budget // cell["lanes"])]
    if cell["transport"] == "quic":
        result += ["--quic-streams", str(cell["streams"])]
    return result


def parse_run(output, clients, cell, phase, validate_sequence=False, window=None, messages=None, pattern=None,
              rtt_interval_ms=None):
    data = []
    for line in output.splitlines():
        if re.match(rf"^\s*{clients},", line):
            try:
                values = [Decimal(value.strip()) for value in line.split(",")]
            except InvalidOperation as error:
                raise ValueError("invalid numeric benchmark row") from error
            if len(values) != 6:
                raise ValueError("unexpected benchmark row")
            if any(not value.is_finite() or value <= 0 or value > 2**63 - 1 for value in values):
                raise ValueError("nonfinite or nonpositive benchmark result")
            if any(values[index] != values[index].to_integral_value() or values[index] > 2**63 - 1
                   for index in [0, 1, 2, 4, 5]):
                raise ValueError("fractional or out-of-range integer benchmark result")
            # Program uses integer elapsed milliseconds for ceil(messages/sec), but reports
            # elapsed time rounded to two decimals. Admit only the possible floor-ms values.
            minimum_ms = math.floor(values[3] - Decimal("0.005"))
            maximum_ms = math.floor(values[3] + Decimal("0.005"))
            if minimum_ms < 1:
                raise ValueError("benchmark duration too short for rate arithmetic")
            numerator = int(values[1]) * 1000
            lower_rate = (numerator + maximum_ms - 1) // maximum_ms
            upper_rate = (numerator + minimum_ms - 1) // minimum_ms
            if not lower_rate <= values[2] <= upper_rate:
                raise ValueError("throughput disagrees with message count and elapsed time")
            data.append(dict(clients=int(values[0]), messages=int(values[1]), messages_per_second=int(values[2]),
                             milliseconds=float(values[3]), start_threads=int(values[4]), end_threads=int(values[5])))
    if len(data) != 2:
        raise ValueError(f"expected warmup + measurement rows, got {len(data)}")
    if "Round-trip check:                  PASSED" not in output:
        raise ValueError("serializer round-trip proof missing")
    if "[ERROR]" in output or "SUSPECT" in output or "WARNING:" in output:
        raise ValueError("error, dropped message or application resend invalidates row")
    size = re.search(r"Serialized payload size:\s+(\d+) bytes", output)
    if size is None or int(size.group(1)) != (352 if validate_sequence else 348):
        raise ValueError("unexpected real V2 payload size")
    if validate_sequence:
        proofs = [json.loads(line.removeprefix("VALIDATION ")) for line in output.splitlines()
                  if line.startswith("VALIDATION ")]
        if len(proofs) != 2 or any(not isinstance(proof, dict) for proof in proofs) \
                or proofs[0].get("phaseId") == proofs[1].get("phaseId"):
            raise ValueError("missing or repeated validation phase")
        expected_count = clients * messages * (1 if pattern == "oneway" else 2)
        expected = dict(version=1, pattern=pattern, clients=clients, messagesPerClient=messages,
                        countedMessages=expected_count, window=window, ackEvery=max(1, window // 2),
                        finalReceipts=clients, payloadBytes=352, sequenceBase=1_000_000_000,
                        preparedPayloads=messages, orderedPayloadsAndContent=True, retries=0)
        if rtt_interval_ms is not None:
            expected["rttIntervalMs"] = rtt_interval_ms
        for row, proof in zip(data, proofs):
            if not isinstance(proof.get("phaseId"), str):
                raise ValueError("validation phase identifier missing")
            uuid.UUID(proof["phaseId"])
            if any(type(proof.get(key)) is not type(value) or proof.get(key) != value
                   for key, value in expected.items()) or row["messages"] != expected_count:
                raise ValueError("validated workload parameters or final delivery proof differ from controller")
            if type(proof.get("datasetAllocatedBytes")) is not int or proof["datasetAllocatedBytes"] <= 0:
                raise ValueError("prepared dataset allocation evidence missing")
            row["validation"] = proof
    reports = [json.loads(line.removeprefix("RTT ")) for line in output.splitlines() if line.startswith("RTT ")]
    if rtt_interval_ms is None and reports:
        raise ValueError("unexpected paced results in an unpaced run")
    if rtt_interval_ms is not None:
        if not validate_sequence or len(reports) != 2:
            raise ValueError("paced results require two ordered validation proofs")
        for row, report in zip(data, reports):
            validate_rtt(report, row["validation"], clients, messages, rtt_interval_ms)
            row["rtt"] = report
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


def validate_rtt(report, proof, clients, messages, interval):
    expected = dict(phaseId=proof["phaseId"], clients=clients, messagesPerClient=messages,
                    samples=clients * messages, intervalMs=interval)
    if not isinstance(report, dict) or any(type(report.get(key)) is not type(value) or report.get(key) != value
                                          for key, value in expected.items()):
        raise ValueError("paced parameters, phase or complete sample count differ")
    def finite(value):
        return type(value) in (int, float) and math.isfinite(value)
    nominal = clients * 1000 / interval
    declared, actual = report.get("nominalRttsPerSecond"), report.get("actualReleaseRttsPerSecond")
    span = report.get("releaseSpanMilliseconds")
    if not finite(declared) or not finite(actual) or actual <= 0 or not math.isclose(declared, nominal, rel_tol=1e-9) \
            or abs(actual / nominal - 1) > 0.05:
        raise ValueError("actual offered rate must be within 5% of the fixed nominal schedule")
    if not finite(span) or span <= 0 or not math.isclose(actual, clients * (messages - 1) * 1000 / span, rel_tol=1e-9):
        raise ValueError("actual offered rate disagrees with release span and count")
    keys = ["p50Us", "p95Us", "p99Us", "maxUs"]
    for group in ["releaseLateness", "dispatchDelay", "sendToEcho", "scheduledToEcho"]:
        distribution = report.get(group)
        if not isinstance(distribution, dict):
            raise ValueError("paced latency distribution missing")
        values = [distribution.get(key) for key in keys]
        if any(not finite(value) or value < 0 for value in values) or values != sorted(values):
            raise ValueError("paced latency quantiles must be finite, nonnegative and ordered")
    for key in keys:
        if report["scheduledToEcho"][key] + 1e-6 < max(report["dispatchDelay"][key], report["sendToEcho"][key]) \
                or report["dispatchDelay"][key] + 1e-6 < report["releaseLateness"][key]:
            raise ValueError("paced latency components contradict end-to-end latency")


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
    paced = {}
    for record in records:
        if record["status"] == "valid" and "rtt" in record["measured"]:
            paced.setdefault(record["cell"], []).append(record["measured"]["rtt"])
    if paced:
        with (output / "rtt-summary.csv").open("w", newline="") as file:
            writer = csv.writer(file)
            writer.writerow(["cell", "runs", "median_actual_offered_rtts_sec", "median_run_p50_us", "median_run_p99_us",
                             "min_run_p99_us", "max_run_p99_us", "median_run_release_p99_us"])
            for name, reports in sorted(paced.items()):
                p99 = [report["scheduledToEcho"]["p99Us"] for report in reports]
                writer.writerow([name, len(reports), statistics.median(report["actualReleaseRttsPerSecond"] for report in reports),
                                 statistics.median(report["scheduledToEcho"]["p50Us"] for report in reports),
                                 statistics.median(p99), min(p99), max(p99),
                                 statistics.median(report["releaseLateness"]["p99Us"] for report in reports)])


def matched_offer_rates(rates):
    return bool(rates) and min(rates) > 0 and all(math.isfinite(value) for value in rates) and max(rates) / min(rates) <= 1.03


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
                    cells=groups, interpretation=("scheduled offered-load latency; message rate is capped, not maximum throughput"
                        if args.rtt_interval_ms is not None else "loopback throughput only") + "; one same-sized warmup before every measured run")
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
                    warmup, measured = parse_run(process.stdout, args.clients, cell, args.phase,
                                                args.validate_sequence, args.window, args.messages, args.pattern,
                                                args.rtt_interval_ms)
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
    if args.rtt_interval_ms is not None:
        comparisons = []
        for repetition in range(args.round_offset + 1, args.round_offset + args.repetitions + 1):
            rates = [record["measured"]["rtt"]["actualReleaseRttsPerSecond"] for record in records
                     if record["repetition"] == repetition and record["status"] == "valid"]
            complete = len(rates) == sum(len(group) for group in groups)
            matched = complete and matched_offer_rates(rates)
            comparisons.append(dict(repetition=repetition, complete=complete, matched=matched, actual_rates=rates))
            invalid |= not matched
        (args.output / "rtt-rate-comparisons.json").write_text(json.dumps(comparisons, indent=2))
    return 1 if invalid else 0


if __name__ == "__main__":
    raise SystemExit(main())
