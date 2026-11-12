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


if __name__ == "__main__":
    unittest.main()
