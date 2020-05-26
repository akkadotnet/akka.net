//-----------------------------------------------------------------------
// <copyright file="SerializationSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Serialization
{
    public class ProgammaticDummy { }
    public class ConfigurationDummy { }

    public class SerializationSetupSpec : AkkaSpec
    {
        public class TestSerializer : Serializer
        {
            public TestSerializer(ExtendedActorSystem system) : base(system)
            {
                IncludeManifest = false;
            }

            public override bool IncludeManifest { get; }

            public override int Identifier => 666;

            private AtomicCounter Counter { get; } = new AtomicCounter(0);
            private ConcurrentDictionary<int, object> Registry { get; } = new ConcurrentDictionary<int, object>();

            public override byte[] ToBinary(object obj)
            {
                var id = Counter.AddAndGet(1);
                Registry.Put(id, obj);
                return BitConverter.GetBytes(id);
            }

            public override object FromBinary(byte[] bytes, Type type)
            {
                if (bytes.Length != 1)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                var id = BitConverter.ToInt32(bytes, 0);
                return Registry[id];
            }
        }


        public static SerializationSetup SerializationSettings = new SerializationSetup(_ => 
            ImmutableHashSet<SerializerDetails>.Empty.Add(SerializerDetails.Create("test", new TestSerializer(_), 
                ImmutableHashSet<Type>.Empty.Add(typeof(ProgammaticDummy)))));

        public static readonly BootstrapSetup Bootstrap = BootstrapSetup.Create().WithConfig(
            ConfigurationFactory.ParseString(@"
            akka{
                actor{
                    serialize-messages = off
                    serialization-bindings {
                      ""Akka.Tests.Serialization.ConfigurationDummy, Akka.Tests"" = test
                    }
                }
            }
        ").WithFallback(TestConfigs.DefaultConfig));

        public static readonly ActorSystemSetup ActorSystemSettings = ActorSystemSetup.Create(SerializationSettings, Bootstrap);

        public SerializationSetupSpec(ITestOutputHelper output) 
            : base(ActorSystem.Create("SerializationSettingsSpec", ActorSystemSettings), output) { }

        private void VerifySerialization(ActorSystem sys, object obj)
        {
            var serialization = sys.Serialization;
            var bytes = serialization.Serialize(obj);
            var serializer = serialization.FindSerializerFor(obj);
            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, obj);
            var deserialized = serialization.Deserialize(bytes, serializer.Identifier, manifest);
            deserialized.Should().Be(obj);
        }

        [Fact]
        public void SerializationSettingsShouldAllowForProgrammaticConfigurationOfSerializers()
        {
            var serializer = Sys.Serialization.FindSerializerFor(new ProgammaticDummy());
            serializer.Should().BeOfType<TestSerializer>();
        }

        [Fact]
        public void SerializationSettingsShouldAllowConfiguredBindingToHookupToProgrammaticSerializer()
        {
            var serializer = Sys.Serialization.FindSerializerFor(new ConfigurationDummy());
            serializer.Should().BeOfType<TestSerializer>();
        }
    }
}
