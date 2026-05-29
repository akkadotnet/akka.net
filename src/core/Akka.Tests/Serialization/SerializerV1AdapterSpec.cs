//-----------------------------------------------------------------------
// <copyright file="SerializerV1AdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Direct unit tests for <see cref="SerializerV1Adapter"/> that exercise the wrapping
    /// behavior independently of <see cref="Serialization"/>'s registration plumbing. Coverage
    /// for the full HOCON/V1 auto-wrap path lives in <see cref="SerializationSpec"/> and
    /// <see cref="CustomSerializerSpec"/>.
    /// </summary>
    public class SerializerV1AdapterSpec : AkkaSpec
    {
        private ExtendedActorSystem ExtSys => (ExtendedActorSystem)Sys;

        // ─── Fixtures ───────────────────────────────────────────────────────────────

        /// <summary>V1 plain Serializer — IncludeManifest = false. Identity transform on byte[].</summary>
        public sealed class PlainV1NoManifest : Serializer
        {
            public PlainV1NoManifest(ExtendedActorSystem system) : base(system) { }

            public override int Identifier => 9001;
            public override bool IncludeManifest => false;

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes((string)obj);
            public override object FromBinary(byte[] bytes, Type? type) => Encoding.UTF8.GetString(bytes);
        }

        /// <summary>V1 plain Serializer — IncludeManifest = true. Manifest is the type qualified name.</summary>
        public sealed class PlainV1WithManifest : Serializer
        {
            public PlainV1WithManifest(ExtendedActorSystem system) : base(system) { }

            public override int Identifier => 9002;
            public override bool IncludeManifest => true;

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(obj.ToString()!);
            public override object FromBinary(byte[] bytes, Type? type)
            {
                var s = Encoding.UTF8.GetString(bytes);
                if (type == typeof(int)) return int.Parse(s);
                if (type == typeof(long)) return long.Parse(s);
                return s;
            }
        }

        /// <summary>V1 SerializerWithStringManifest. Custom manifest dispatch.</summary>
        public sealed class V1WithStringManifest : SerializerWithStringManifest
        {
            public V1WithStringManifest(ExtendedActorSystem system) : base(system) { }

            public override int Identifier => 9003;

            public override string Manifest(object o) => o switch
            {
                int _ => "I",
                string _ => "S",
                _ => throw new ArgumentException($"Unknown {o.GetType()}")
            };

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(obj.ToString()!);
            public override object FromBinary(byte[] bytes, string manifest) => manifest switch
            {
                "I" => int.Parse(Encoding.UTF8.GetString(bytes)),
                "S" => Encoding.UTF8.GetString(bytes),
                _ => throw new ArgumentException($"Unknown manifest [{manifest}]")
            };
        }

        // ─── Plain V1, IncludeManifest = false ──────────────────────────────────────

        [Fact]
        public void Adapter_should_round_trip_through_buffer_API_for_plain_V1_no_manifest()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            var buffer = new ArrayBufferWriter<byte>();
            adapter.Serialize(buffer, "hello world");
            var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
            var roundTripped = adapter.Deserialize(seq, adapter.Manifest("hello world"));

            roundTripped.Should().Be("hello world");
        }

        [Fact]
        public void Adapter_Manifest_should_be_empty_for_plain_V1_no_manifest()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.Manifest("anything").Should().BeEmpty();
        }

        // ─── Plain V1, IncludeManifest = true ───────────────────────────────────────

        [Fact]
        public void Adapter_Manifest_should_be_TypeQualifiedName_for_plain_V1_with_manifest()
        {
            var inner = new PlainV1WithManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.Manifest(42).Should().Be(typeof(int).TypeQualifiedName());
        }

        [Fact]
        public void Adapter_should_round_trip_via_FromBinary_Type_for_plain_V1_with_manifest()
        {
            var inner = new PlainV1WithManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            var bytes = adapter.ToBinary(42);
            var roundTripped = adapter.FromBinary(bytes, typeof(int));

            roundTripped.Should().Be(42);
        }

        // ─── V1 SerializerWithStringManifest ────────────────────────────────────────

        [Fact]
        public void Adapter_should_delegate_Manifest_to_inner_SerializerWithStringManifest()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.Manifest(42).Should().Be("I");
            adapter.Manifest("hi").Should().Be("S");
        }

        [Fact]
        public void Adapter_should_round_trip_through_buffer_API_for_V1_with_string_manifest()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            var buffer = new ArrayBufferWriter<byte>();
            adapter.Serialize(buffer, 12345);
            var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
            var roundTripped = adapter.Deserialize(seq, adapter.Manifest(12345));

            roundTripped.Should().Be(12345);
        }

        [Fact]
        public void Adapter_should_round_trip_through_byte_array_bridge_for_V1_with_string_manifest()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            var bytes = adapter.ToBinary("buffered");
            var roundTripped = adapter.FromBinary(bytes, "S");

            roundTripped.Should().Be("buffered");
        }

        // ─── Identity preservation ──────────────────────────────────────────────────

        [Fact]
        public void Adapter_Identifier_should_match_inner_serializer()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.Identifier.Should().Be(inner.Identifier);
        }

        [Fact]
        public void Adapter_Inner_should_return_the_wrapped_instance_unchanged()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.Inner.Should().BeSameAs(inner);
        }

        // ─── Bridge overrides (no buffer round trip) ────────────────────────────────

        [Fact]
        public void Adapter_ToBinary_should_produce_byte_identical_output_to_inner_V1()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.ToBinary(99).Should().Equal(inner.ToBinary(99));
        }

        [Fact]
        public void Adapter_FromBinary_should_produce_identical_output_to_inner_V1()
        {
            var inner = new V1WithStringManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            var bytes = inner.ToBinary(99);

            adapter.FromBinary(bytes, "I").Should().Be(inner.FromBinary(bytes, "I"));
        }

        // ─── Multi-segment Deserialize ──────────────────────────────────────────────

        [Fact]
        public void Adapter_should_handle_multi_segment_ReadOnlySequence_input()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            var adapter = new SerializerV1Adapter(ExtSys, inner);

            // Encode "split me up" and split into two segments mid-string.
            var bytes = inner.ToBinary("split me up");
            var first = new MemorySegment(bytes.AsMemory(0, 4));
            var second = first.Append(bytes.AsMemory(4));
            var seq = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
            seq.IsSingleSegment.Should().BeFalse();

            var roundTripped = adapter.Deserialize(seq, string.Empty);

            roundTripped.Should().Be("split me up");
        }

        // ─── AsV1 / TryAsV1 extensions ──────────────────────────────────────────────

        [Fact]
        public void AsV1_should_unwrap_to_inner_when_types_match()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            SerializerV2 adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.AsV1<PlainV1NoManifest>().Should().BeSameAs(inner);
        }

        [Fact]
        public void AsV1_should_throw_when_types_do_not_match()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            SerializerV2 adapter = new SerializerV1Adapter(ExtSys, inner);

            Action act = () => adapter.AsV1<V1WithStringManifest>();
            act.Should().Throw<InvalidCastException>();
        }

        [Fact]
        public void TryAsV1_should_return_null_when_types_do_not_match()
        {
            var inner = new PlainV1NoManifest(ExtSys);
            SerializerV2 adapter = new SerializerV1Adapter(ExtSys, inner);

            adapter.TryAsV1<V1WithStringManifest>().Should().BeNull();
        }

        // ─── Helper: minimal segmented sequence builder ─────────────────────────────

        private sealed class MemorySegment : ReadOnlySequenceSegment<byte>
        {
            public MemorySegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public MemorySegment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new MemorySegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }
    }
}
