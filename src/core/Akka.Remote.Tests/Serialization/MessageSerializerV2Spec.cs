//-----------------------------------------------------------------------
// <copyright file="MessageSerializerV2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class MessageSerializerV2Spec: AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString($@"
            akka.actor {{
                serializers {{
                    native-v2 = ""{typeof(NativeV2Serializer).AssemblyQualifiedName}""
                }}
                serialization-bindings {{
                    ""{typeof(NativeV2Message).AssemblyQualifiedName}"" = native-v2
                }}
            }}");

        public MessageSerializerV2Spec(ITestOutputHelper output) : base(output, Config)
        {
        }

        [Fact]
        public void Classic_MessageSerializer_should_roundtrip_native_v2_payloads()
        {
            var message = new NativeV2Message("classic remoting v2");
            var address = new Address("akka.tcp", "Sys", "localhost", 2551);
            var info = new Information(address, Sys);

            var serialized = MessageSerializer.Serialize((ExtendedActorSystem)Sys, info, message);

            serialized.SerializerId.Should().Be(NativeV2Serializer.SerializerId);
            serialized.MessageManifest.ToStringUtf8().Should().Be(NativeV2Serializer.NativeManifest);
            serialized.Message.ToByteArray().Should().Equal(Encoding.UTF8.GetBytes(message.Value));
            MessageSerializer.Deserialize((ExtendedActorSystem)Sys, serialized).Should().Be(message);
        }

        [Fact]
        public void Classic_MessageSerializer_should_preserve_legacy_byte_array_wire_metadata()
        {
            var message = new byte[] { 1, 2, 3, 4 };
            var address = new Address("akka.tcp", "Sys", "localhost", 2551);
            var info = new Information(address, Sys);

            var serialized = MessageSerializer.Serialize((ExtendedActorSystem)Sys, info, message);

            serialized.SerializerId.Should().Be(4);
            serialized.MessageManifest.IsEmpty.Should().BeTrue();
            serialized.Message.ToByteArray().Should().Equal(message);

            var deserialized = MessageSerializer.Deserialize((ExtendedActorSystem)Sys, serialized);
            deserialized.Should().BeOfType<byte[]>();
            ((byte[])deserialized).Should().Equal(message);
        }

        [Fact]
        public void WrappedPayloadSupport_should_preserve_payload_serializer_metadata()
        {
            var message = new NativeV2Message("wrapped v2");
            var support = new Akka.Remote.Serialization.WrappedPayloadSupport((ExtendedActorSystem)Sys);

            var payload = support.PayloadToProto(message);

            payload.SerializerId.Should().Be(NativeV2Serializer.SerializerId);
            payload.MessageManifest.ToStringUtf8().Should().Be(NativeV2Serializer.NativeManifest);
            payload.Message.ToByteArray().Should().Equal(Encoding.UTF8.GetBytes(message.Value));
            support.PayloadFrom(payload).Should().Be(message);
        }

        public sealed class NativeV2Message: IEquatable<NativeV2Message>
        {
            public NativeV2Message(string value)
            {
                Value = value;
            }

            public string Value { get; }

            public bool Equals(NativeV2Message other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj.GetType() == GetType() && Equals((NativeV2Message)obj);
            }

            public override int GetHashCode()
            {
                return Value.GetHashCode();
            }
        }

        public sealed class NativeV2Serializer : SerializerV2
        {
            public const int SerializerId = 77125;
            public const string NativeManifest = "native-v2-message";

            public NativeV2Serializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => SerializerId;

            public override string Manifest(object obj) => NativeManifest;

            public override int SizeHint(object obj)
            {
                return Encoding.UTF8.GetByteCount(((NativeV2Message)obj).Value);
            }

            public override int Serialize(object obj, IBufferWriter<byte> writer)
            {
                var value = ((NativeV2Message)obj).Value;
                var byteCount = Encoding.UTF8.GetByteCount(value);
                var span = writer.GetSpan(byteCount);
                var written = Encoding.UTF8.GetBytes(value, span);
                writer.Advance(written);
                return written;
            }

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
            {
                if (manifest != NativeManifest)
                    throw new SerializationException($"Unknown manifest [{manifest}]");
                return new NativeV2Message(Encoding.UTF8.GetString(bytes.ToArray()));
            }
        }
    }
}
