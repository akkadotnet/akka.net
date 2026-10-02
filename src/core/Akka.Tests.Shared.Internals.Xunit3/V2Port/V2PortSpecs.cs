//-----------------------------------------------------------------------
// <copyright file="V2PortSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using FluentAssertions;

namespace Akka.Serialization
{
    /// <summary>
    /// The checks every V2 serializer port runs, so the ports don't each carry a copy. Each method takes the
    /// system the port's serializers are registered on, and a <see cref="V2Port"/> that names the legacy serializer,
    /// the V2 serializer and the corpus. A failed check throws, naming the corpus case.
    /// </summary>
    /// <remarks>
    /// <see cref="V2PortSpec"/> wraps all of these in ready-made tests. Call the methods directly when a port needs
    /// a different shape, or to build a test that runs before the V2 serializer exists (see
    /// <see cref="AssertLegacyBytesMatchGolden"/>).
    /// </remarks>
    public static class V2PortSpecs
    {
        private const string DynamicTypeLoadingSwitch = "Akka.DynamicTypeLoading";

        /// <summary>
        /// The corpus itself is sound: it has cases, their names are unique (they name the golden files), and it
        /// covers every manifest in <see cref="V2Port.RequiredManifests"/>.
        /// </summary>
        public static void AssertCorpus(V2Port port)
        {
            port.Cases.Should().NotBeEmpty("a port needs a corpus");

            var duplicates = port.Cases.GroupBy(c => c.FileStem, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            duplicates.Should().BeEmpty("case names become golden file names, so each must be unique; pass a name to V2PortCase");

            var missing = port.RequiredManifests.Except(port.Cases.Select(c => c.Manifest)).ToList();
            missing.Should().BeEmpty("the corpus must cover every manifest the legacy serializer handles");
        }

        /// <summary>
        /// The V2 id is the legacy id plus <see cref="V2Port.IdOffset"/>, and it sits inside the reserved V2 block
        /// (<see cref="V2Port.V2IdMin"/> to <see cref="V2Port.V2IdMax"/>), clear of the legacy id.
        /// </summary>
        public static void AssertIds(ActorSystem system, V2Port port)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);
            var v2 = port.CreateV2((ExtendedActorSystem)system);

            v2.Identifier.Should().Be(legacy.Identifier + port.IdOffset,
                $"the V2 id is the legacy id {legacy.Identifier} plus {port.IdOffset}");
            v2.Identifier.Should().BeInRange(port.V2IdMin, port.V2IdMax, "V2 ports use the reserved block");
            legacy.Identifier.Should().BeLessThan(port.V2IdMin, "the legacy id sits below the V2 block");
        }

        /// <summary>
        /// Both serializers give every corpus message the manifest the case names, so the V2 manifests are the
        /// legacy tokens.
        /// </summary>
        public static void AssertManifestParity(ActorSystem system, V2Port port)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);
            var v2 = port.CreateV2((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
            {
                legacy.Manifest(c.Message).Should().Be(c.Manifest, Why($"the legacy manifest of {c}"));
                v2.Manifest(c.Message).Should().Be(c.Manifest, Why($"V2 must reuse the legacy manifest token for {c}"));
            }
        }

        /// <summary>
        /// Every corpus message survives the V2 serializer: <c>ToBinary</c> and <c>FromBinary</c>, and, for a
        /// <see cref="SerializerV2"/>, <c>Serialize</c> into a buffer writer and <c>Deserialize</c> from a sequence.
        /// The buffer path must write the same bytes, and a <see cref="SerializerV2.SizeHint"/> must be exact.
        /// </summary>
        public static void AssertV2RoundTrips(ActorSystem system, V2Port port)
        {
            var v2 = port.CreateV2((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
                RoundTrip(v2, c, "V2");
        }

        /// <summary>The legacy serializer still round trips every corpus message: the port took nothing away.</summary>
        public static void AssertLegacyRoundTrips(ActorSystem system, V2Port port)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
                RoundTrip(legacy, c, "legacy");
        }

        /// <summary>
        /// One node reads both wires: <c>system.Serialization.Deserialize(bytes, id, manifest)</c> works for the
        /// legacy id and the V2 id, which is what lets a rolling upgrade run V2 nodes beside legacy ones.
        /// </summary>
        public static void AssertBothIdsDecode(ActorSystem system, V2Port port)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);
            var v2 = port.CreateV2((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
            {
                var legacyRestored = system.Serialization.Deserialize(legacy.ToBinary(c.Message), legacy.Identifier, c.Manifest);
                AssertEquivalent(c, legacyRestored, $"decoded by legacy id {legacy.Identifier}");

                var v2Bytes = v2.ToBinary(c.Message);
                AssertEquivalent(c, system.Serialization.Deserialize(v2Bytes, v2.Identifier, c.Manifest),
                    $"decoded by V2 id {v2.Identifier}");
                AssertEquivalent(c, system.Serialization.Deserialize(new ReadOnlySequence<byte>(v2Bytes), v2.Identifier, c.Manifest),
                    $"decoded by V2 id {v2.Identifier} from a sequence");
            }
        }

        /// <summary>
        /// <c>FindSerializerFor</c> and <c>FindSerializerForType</c> give the serializer <see cref="V2Port.ExpectedBinding"/>
        /// names - the legacy one while the V2 row is read-only, so a port changes no bytes on the wire.
        /// </summary>
        public static void AssertBinding(ActorSystem system, V2Port port)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);
            var v2 = port.CreateV2((ExtendedActorSystem)system);
            var expected = port.ExpectedBinding == V2PortBinding.Legacy ? legacy : v2;
            var other = port.ExpectedBinding == V2PortBinding.Legacy ? v2 : legacy;

            foreach (var c in port.Cases)
            {
                foreach (var found in new[] { system.Serialization.FindSerializerFor(c.Message), system.Serialization.FindSerializerForType(c.Message.GetType()) })
                {
                    found.GetType().Should().Be(expected.GetType(), Why($"{c} should resolve to the {port.ExpectedBinding} serializer"));
                    found.Identifier.Should().Be(expected.Identifier);
                    found.GetType().Should().NotBe(other.GetType());
                }
            }
        }

        /// <summary>
        /// With the <c>Akka.DynamicTypeLoading</c> switch off for the whole check, a fresh <see cref="Serialization"/>
        /// still resolves the V2 id, and every corpus message round trips through it: nothing on the V2 path needs
        /// <see cref="Type.GetType(string)"/>.
        /// </summary>
        public static void AssertV2ResolvesWithDynamicTypeLoadingOff(ActorSystem system, V2Port port)
        {
            using (HoldDynamicTypeLoading(enabled: false))
            {
                var serialization = new Serialization((ExtendedActorSystem)system);
                var v2 = port.CreateV2((ExtendedActorSystem)system);

                foreach (var c in port.Cases)
                {
                    var bytes = v2.ToBinary(c.Message);
                    AssertEquivalent(c, serialization.Deserialize(bytes, v2.Identifier, c.Manifest),
                        $"decoded by V2 id {v2.Identifier} with dynamic type loading off");
                    RoundTrip(v2, c, "V2 (dynamic type loading off)");
                }
            }
        }

        /// <summary>
        /// The V2 bytes of every corpus message match <paramref name="golden"/>. Fails with a hex diff when the wire
        /// format changes. Capture the files with <see cref="GoldenBytes.CaptureVariable"/>=v2.
        /// </summary>
        public static void AssertV2BytesMatchGolden(ActorSystem system, V2Port port, GoldenBytes golden)
        {
            var v2 = port.CreateV2((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
                golden.Check(c.Name, v2.ToBinary(c.Message), Describe(v2, c));
        }

        /// <summary>
        /// The legacy bytes of every corpus message match <paramref name="golden"/>. This needs no V2 serializer, so a
        /// porter runs it first, with <see cref="GoldenBytes.CaptureVariable"/>=legacy, before the port touches anything,
        /// and checks the files in. Afterwards it proves the legacy writer still writes the same bytes.
        /// </summary>
        /// <param name="system">A system to build the serializer on.</param>
        /// <param name="legacy">The legacy serializer.</param>
        /// <param name="cases">The corpus.</param>
        /// <param name="golden">The <see cref="GoldenKind.Legacy"/> folder.</param>
        /// <param name="compareBytes">
        /// False for a legacy serializer whose output isn't deterministic: only a capture writes files, a check does nothing.
        /// </param>
        public static void AssertLegacyBytesMatchGolden(
            ActorSystem system, Serializer legacy, IEnumerable<V2PortCase> cases, GoldenBytes golden, bool compareBytes = true)
        {
            _ = system;
            foreach (var c in cases)
            {
                if (golden.Capture || compareBytes)
                    golden.Check(c.Name, legacy.ToBinary(c.Message), Describe(legacy, c));
            }
        }

        /// <summary>
        /// The legacy bytes of <see cref="AssertLegacyBytesMatchGolden"/> were written before the port. A node that holds
        /// both serializers still decodes them through the legacy id, into messages equal to the corpus.
        /// </summary>
        public static void AssertNodeDecodesLegacyGolden(ActorSystem system, V2Port port, GoldenBytes golden)
        {
            var legacy = port.CreateLegacy((ExtendedActorSystem)system);

            foreach (var c in port.Cases)
            {
                var restored = system.Serialization.Deserialize(golden.Read(c.Name), legacy.Identifier, c.Manifest);
                AssertEquivalent(c, restored, "decoded from the golden legacy bytes");
            }
        }

        /// <summary>
        /// Holds the <c>Akka.DynamicTypeLoading</c> AppContext switch at <paramref name="enabled"/> until the result is
        /// disposed, then puts back what was there. The switch is process-wide, so don't run this beside tests that
        /// depend on it; the test assemblies that use the kit don't run test collections in parallel.
        /// </summary>
        public static IDisposable HoldDynamicTypeLoading(bool enabled)
        {
            var hadSwitch = AppContext.TryGetSwitch(DynamicTypeLoadingSwitch, out var previous);
            AppContext.SetSwitch(DynamicTypeLoadingSwitch, enabled);
            return new SwitchScope(hadSwitch, previous);
        }

        /// <summary>
        /// Asserts <paramref name="actual"/> is the corpus message of <paramref name="c"/>: by the case's comparison if it
        /// has one, otherwise <see cref="object.Equals(object)"/>, otherwise structural equivalence.
        /// </summary>
        public static void AssertEquivalent(V2PortCase c, object? actual, string stage)
        {
            actual.Should().NotBeNull(Why($"{stage}: {c}"));
            if (c.Equivalence is not null)
            {
                c.Equivalence(c.Message, actual!);
                return;
            }

            if (Equals(c.Message, actual))
                return;

            actual.Should().BeEquivalentTo(c.Message, options => options.RespectingRuntimeTypes(), "{0}: {1}", stage, c);
        }

        private static void RoundTrip(Serializer serializer, V2PortCase c, string label)
        {
            var bytes = serializer.ToBinary(c.Message);
            AssertEquivalent(c, serializer.FromBinary(bytes, c.Manifest), $"{label} FromBinary");

            if (serializer is not SerializerV2 v2)
                return;

            var writer = new ArrayBufferWriter<byte>();
            var written = v2.Serialize(c.Message, writer);
            written.Should().Be(writer.WrittenCount, Why($"{label} Serialize must report the bytes it wrote for {c}"));
            writer.WrittenSpan.ToArray().Should().Equal(bytes, Why($"{label} Serialize and ToBinary must write the same bytes for {c}"));

            var hint = v2.SizeHint(c.Message);
            if (hint != SerializerV2.UnknownSize)
                hint.Should().Be(bytes.Length, Why($"{label} SizeHint must be exact for {c}"));

            AssertEquivalent(c, v2.Deserialize(new ReadOnlySequence<byte>(bytes), c.Manifest), $"{label} Deserialize");
        }

        // FluentAssertions runs "because" through string.Format when there are no args, so braces in a case name need escaping
        private static string Why(string reason) => reason.Replace("{", "{{").Replace("}", "}}");

        private static string[] Describe(Serializer serializer, V2PortCase c)
            => new[]
            {
                $"serializer: {serializer.GetType().Name}, id {serializer.Identifier}",
                $"manifest: \"{c.Manifest}\"",
                $"message: {c.Message.GetType().Name}"
            };

        private sealed class SwitchScope : IDisposable
        {
            private readonly bool _hadSwitch;
            private readonly bool _previous;

            public SwitchScope(bool hadSwitch, bool previous)
            {
                _hadSwitch = hadSwitch;
                _previous = previous;
            }

            // an unset switch means "enabled", the same as AkkaFeatures reads it
            public void Dispose() => AppContext.SetSwitch(DynamicTypeLoadingSwitch, !_hadSwitch || _previous);
        }
    }
}
