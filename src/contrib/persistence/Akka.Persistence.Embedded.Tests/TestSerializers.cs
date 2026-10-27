//-----------------------------------------------------------------------
// <copyright file="TestSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Embedded.Tests
{
    public sealed record TestEvent(string Value);

    /// <summary>Serializes to a zero-length byte array.</summary>
    public sealed record EmptyPayload;

    /// <summary>Handled by a serializer that has no string manifest and IncludeManifest = true.</summary>
    public sealed record ObjectManifestEvent(string Value);

    /// <summary>Handled by a serializer that has no manifest at all.</summary>
    public sealed record NoManifestEvent(string Value);

    /// <summary>Has no serialization binding. Only the <c>serializer</c> setting of a plugin reaches its serializer.</summary>
    public sealed record UnboundEvent(string Value);

    /// <summary>Its serializer always throws.</summary>
    public sealed record UnserializableEvent(string Value);

    public sealed class TestEventSerializer : SerializerWithStringManifest
    {
        public TestEventSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7301;

        public override string Manifest(object o)
            => o switch
            {
                TestEvent => "E",
                EmptyPayload => "Z",
                _ => throw new ArgumentException($"Unsupported type {o.GetType()}")
            };

        public override byte[] ToBinary(object obj)
            => obj switch
            {
                TestEvent e => Encoding.UTF8.GetBytes(e.Value),
                EmptyPayload => Array.Empty<byte>(),
                _ => throw new ArgumentException($"Unsupported type {obj.GetType()}")
            };

        public override object FromBinary(byte[] bytes, string manifest)
            => manifest switch
            {
                "E" => new TestEvent(Encoding.UTF8.GetString(bytes)),
                "Z" => new EmptyPayload(),
                _ => throw new ArgumentException($"Unknown manifest {manifest}")
            };
    }

    public sealed class ObjectManifestSerializer : Serializer
    {
        public ObjectManifestSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7302;

        public override bool IncludeManifest => true;

        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((ObjectManifestEvent)obj).Value);

        public override object FromBinary(byte[] bytes, Type type) => new ObjectManifestEvent(Encoding.UTF8.GetString(bytes));
    }

    public sealed class NoManifestSerializer : Serializer
    {
        public NoManifestSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7303;

        public override bool IncludeManifest => false;

        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((NoManifestEvent)obj).Value);

        public override object FromBinary(byte[] bytes, Type? type) => new NoManifestEvent(Encoding.UTF8.GetString(bytes));
    }

    public sealed class ThrowingSerializer : Serializer
    {
        public ThrowingSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7304;

        public override bool IncludeManifest => false;

        public override byte[] ToBinary(object obj) => throw new InvalidOperationException("This payload cannot be serialized.");

        public override object FromBinary(byte[] bytes, Type? type) => throw new InvalidOperationException("never");
    }

    /// <summary>Registered under a name but bound to no type: the target of the plugins' <c>serializer</c> setting.</summary>
    public sealed class UnboundEventSerializer : SerializerWithStringManifest
    {
        public UnboundEventSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7305;

        public override string Manifest(object o) => "U";

        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UnboundEvent)obj).Value);

        public override object FromBinary(byte[] bytes, string manifest) => new UnboundEvent(Encoding.UTF8.GetString(bytes));
    }

    public static class TestSerializerConfig
    {
        public static string Hocon { get; } = $$"""
            akka.actor {
                serializers {
                    test-event = "{{typeof(TestEventSerializer).AssemblyQualifiedName}}"
                    object-manifest = "{{typeof(ObjectManifestSerializer).AssemblyQualifiedName}}"
                    no-manifest = "{{typeof(NoManifestSerializer).AssemblyQualifiedName}}"
                    throwing = "{{typeof(ThrowingSerializer).AssemblyQualifiedName}}"
                    unbound-event = "{{typeof(UnboundEventSerializer).AssemblyQualifiedName}}"
                }
                serialization-bindings {
                    "{{typeof(TestEvent).AssemblyQualifiedName}}" = test-event
                    "{{typeof(EmptyPayload).AssemblyQualifiedName}}" = test-event
                    "{{typeof(ObjectManifestEvent).AssemblyQualifiedName}}" = object-manifest
                    "{{typeof(NoManifestEvent).AssemblyQualifiedName}}" = no-manifest
                    "{{typeof(UnserializableEvent).AssemblyQualifiedName}}" = throwing
                }
            }
            """;
    }
}
