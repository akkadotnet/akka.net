#!/usr/bin/env python3
"""Focused fail-closed checks for the optional ordered-delivery result contract."""

import copy
import json
from types import SimpleNamespace
import unittest
import uuid

import compare_transports as controller


class ValidationContractTests(unittest.TestCase):
    def setUp(self):
        self.cell = dict(transport="dotnetty", flags=["--transport", "dotnetty"], binary="benchmark.dll")
        self.proofs = [dict(version=1, phaseId=str(uuid.uuid4()), pattern="oneway", clients=2,
                            messagesPerClient=8193, countedMessages=16386, window=10, ackEvery=5,
                            finalReceipts=2, payloadBytes=352, sequenceBase=1_000_000_000,
                            preparedPayloads=8193, datasetAllocatedBytes=1000,
                            orderedPayloadsAndContent=True, retries=0) for _ in range(2)]

    def output(self, proofs=None):
        lines = ["Round-trip check:                  PASSED", "Serialized payload size: 352 bytes"]
        lines += ["2,16386,5001,3277.2,20,20"] * 2
        lines += ["VALIDATION " + json.dumps(proof) for proof in (self.proofs if proofs is None else proofs)]
        return "\n".join(lines)

    def parse(self, output):
        return controller.parse_run(output, 2, self.cell, "transports", True, 10, 8193, "oneway")

    def test_complete_distinct_phase_proofs_are_retained(self):
        warmup, measured = self.parse(self.output())
        self.assertEqual(warmup["validation"], self.proofs[0])
        self.assertEqual(measured["validation"], self.proofs[1])

    def test_missing_repeated_or_malformed_proof_fails(self):
        for proofs in [[], self.proofs[:1], [self.proofs[0]] * 2, [[], {}], [{}, self.proofs[1]]]:
            with self.subTest(proofs=proofs), self.assertRaises(ValueError):
                self.parse(self.output(proofs))

    def test_changed_parameters_and_false_confirmation_fail(self):
        changes = dict(version=2, phaseId="invalid", pattern="ping-pong", clients=3, messagesPerClient=8192,
                       countedMessages=16385, window=11, ackEvery=6, finalReceipts=1, payloadBytes=348,
                       sequenceBase=0, preparedPayloads=8192, datasetAllocatedBytes=0,
                       orderedPayloadsAndContent=False, retries=1)
        for key, value in changes.items():
            proofs = copy.deepcopy(self.proofs)
            proofs[1][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.parse(self.output(proofs))

    def test_missing_fields_and_wrong_json_types_fail(self):
        for key in self.proofs[0]:
            proofs = copy.deepcopy(self.proofs)
            del proofs[0][key]
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.parse(self.output(proofs))
        proofs = copy.deepcopy(self.proofs)
        proofs[0]["version"] = True
        with self.assertRaises(ValueError):
            self.parse(self.output(proofs))

    def test_legacy_count_mode_does_not_require_new_proofs(self):
        output = self.output([]).replace("352 bytes", "348 bytes")
        rows = controller.parse_run(output, 2, self.cell, "regression")
        self.assertEqual(len(rows), 2)
        self.assertNotIn("validation", rows[1])

    def test_normal_command_passes_the_opt_in_flag(self):
        args = SimpleNamespace(dotnet="dotnet", clients=2, window=10, messages=8193,
                               pattern="oneway", validate_sequence=True)
        self.assertIn("--validate-sequence", controller.command(args, self.cell))
        args.validate_sequence = False
        self.assertNotIn("--validate-sequence", controller.command(args, self.cell))

    def test_numeric_corruption_fails(self):
        for bad in ["2,16386.5,5001,3277.2,20,20", "2,16386,5001.5,3277.2,20,20",
                    "2,16386,5001,NaN,20,20", "2,16386,5001,Infinity,20,20",
                    "2,16386,5001,-1,20,20", "2,16386,9000,3277.2,20,20",
                    "2,16386,5001,3277.2,20.5,20", "2,16386,5001,3277.2,20,0"]:
            with self.subTest(row=bad), self.assertRaises(ValueError):
                self.parse(self.output().replace("2,16386,5001,3277.2,20,20", bad, 1))


class PacedContractTests(unittest.TestCase):
    def setUp(self):
        self.proof = dict(phaseId=str(uuid.uuid4()))
        self.report = dict(phaseId=self.proof["phaseId"], clients=2, messagesPerClient=3, samples=6,
                           intervalMs=2, nominalRttsPerSecond=1000, actualReleaseRttsPerSecond=1000,
                           releaseSpanMilliseconds=4)
        for group, values in [("releaseLateness", [1, 2, 3, 4]), ("dispatchDelay", [2, 3, 4, 5]),
                              ("sendToEcho", [10, 20, 30, 40]), ("scheduledToEcho", [12, 23, 34, 45])]:
            self.report[group] = dict(zip(["p50Us", "p95Us", "p99Us", "maxUs"], values))

    def validate(self, report):
        controller.validate_rtt(report, self.proof, 2, 3, 2)

    def test_complete_consistent_report_passes(self):
        self.validate(self.report)

    def test_missing_or_wrong_phase_and_parameters_fail(self):
        for key in self.report:
            report = copy.deepcopy(self.report)
            del report[key]
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.validate(report)
        for key, value in dict(phaseId=str(uuid.uuid4()), samples=5, clients=3, intervalMs=3,
                               messagesPerClient=4, nominalRttsPerSecond=900, actualReleaseRttsPerSecond=900,
                               releaseSpanMilliseconds=3).items():
            report = copy.deepcopy(self.report)
            report[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.validate(report)

    def test_nonfinite_negative_unordered_and_inconsistent_quantiles_fail(self):
        for group in ["releaseLateness", "dispatchDelay", "sendToEcho", "scheduledToEcho"]:
            for value in [float("nan"), float("inf"), -1, True]:
                report = copy.deepcopy(self.report)
                report[group]["p50Us"] = value
                with self.subTest(group=group, value=value), self.assertRaises(ValueError):
                    self.validate(report)
        report = copy.deepcopy(self.report)
        report["scheduledToEcho"]["p99Us"] = 22
        with self.assertRaises(ValueError): self.validate(report)
        report = copy.deepcopy(self.report)
        report["dispatchDelay"] = dict(p50Us=0, p95Us=0, p99Us=0, maxUs=0)
        with self.assertRaises(ValueError): self.validate(report)

    def test_nonfinite_or_false_rate_and_span_fail(self):
        for key in ["nominalRttsPerSecond", "actualReleaseRttsPerSecond", "releaseSpanMilliseconds"]:
            for value in [float("nan"), float("inf"), 0, True]:
                report = copy.deepcopy(self.report)
                report[key] = value
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    self.validate(report)

    def test_matched_offer_rate_gate_rejects_comparison_drift(self):
        self.assertTrue(controller.matched_offer_rates([1000, 1020]))
        self.assertFalse(controller.matched_offer_rates([1000, 1040]))
        self.assertFalse(controller.matched_offer_rates([]))
        self.assertFalse(controller.matched_offer_rates([1000, float("nan")]))

    def test_public_parser_requires_interval_and_two_linked_rtt_reports(self):
        fixture = ValidationContractTests()
        fixture.setUp()
        proofs = copy.deepcopy(fixture.proofs)
        reports = []
        for proof in proofs:
            proof.update(pattern="ping-pong", rttIntervalMs=2, countedMessages=32772)
            report = copy.deepcopy(self.report)
            report.update(phaseId=proof["phaseId"], messagesPerClient=8193, samples=16386,
                          releaseSpanMilliseconds=(8193 - 1) * 2)
            reports.append(report)
        output = fixture.output(proofs).replace("2,16386,5001,3277.2,20,20", "2,32772,10001,3277.2,20,20")
        output += "\n" + "\n".join("RTT " + json.dumps(report) for report in reports)
        args = (2, fixture.cell, "transports", True, 10, 8193, "ping-pong", 2)
        self.assertEqual(len(controller.parse_run(output, *args)), 2)
        with self.assertRaises(ValueError):
            controller.parse_run(output[:output.rfind("RTT ")], *args)
        with self.assertRaises(ValueError):
            controller.parse_run(output.replace('"rttIntervalMs": 2', '"rttIntervalMs": 3', 1), *args)
        quic = dict(transport="quic", lanes=1, streams=1)
        quic_output = output + "\n" + "\n".join([
            "Recipient lane occupancy (send): 2", "Recipient lane occupancy (reply): 2",
            "QUIC ordinary streams open: 1 (per-stream inbound lanes 1; receive paths 1)",
            "QUIC reply ordinary streams open: 1"] * 2)
        quic_args = (2, quic, "transports", True, 10, 8193, "ping-pong", 2)
        self.assertEqual(len(controller.parse_run(quic_output, *quic_args)), 2)
        with self.assertRaises(ValueError):
            controller.parse_run(quic_output.replace("QUIC reply ordinary streams open: 1", ""), *quic_args)
        with self.assertRaises(ValueError):
            controller.parse_run(quic_output.replace("QUIC reply ordinary streams open: 1", "QUIC reply ordinary streams open: 2", 1), *quic_args)


if __name__ == "__main__":
    unittest.main()
