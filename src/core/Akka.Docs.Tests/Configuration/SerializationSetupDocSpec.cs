//-----------------------------------------------------------------------
// <copyright file="SerializationSetupDocSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit.Configs;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Configuration
{
    // <Protocol>
    public interface IAppProtocol{}

    public sealed class Ack : IAppProtocol{ }

    public sealed class Nack : IAppProtocol{ }
    // </Protocol>

    // <Serializer>
    public sealed class AppProtocolSerializer : SerializerWithStringManifest
    {
        public AppProtocolSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// Pick a custom value between 100-1000 - this gets included in the manifests
        /// that are sent via Akka.Remote and Akka.Persistence, so it's important to pick
        /// a unique and stable value for each custom serializer.
        /// </summary>
        public override int Identifier => 588;

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                // no dynamic content here - manifest is enough to tell us what message type is
                // so no need to populate byte array
                case Ack _:
                    return Array.Empty<byte>();
                case Nack _:
                    return Array.Empty<byte>();
                default:
                    throw new NotImplementedException($"Unsupported serialization type [{obj.GetType()}]");
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case "A":
                    return new Ack();
                case "N":
                    return new Nack();
                default:
                    throw new NotImplementedException($"Unsupported serialization manifest [{manifest}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case Ack _:
                    return "A";
                case Nack _:
                    return "N";
                default:
                    throw new NotImplementedException($"Unsupported serialization type [{o.GetType()}]");
            }
        }
    }
    // </Serializer>

    public class SerializationSetupDocSpec
    {
        // <SerializerSetup>
        public static SerializationSetup SerializationSettings => 
            SerializationSetup.Create(actorSystem => 
                    ImmutableHashSet<SerializerDetails>.Empty.Add(
                        SerializerDetails.Create("app-protocol", 
                            new AppProtocolSerializer(actorSystem), 
                            ImmutableHashSet<Type>.Empty.Add(typeof(IAppProtocol)))));
        // </SerializerSetup>

        // <MergedSetup>
        public static readonly BootstrapSetup Bootstrap = BootstrapSetup.Create().WithConfig(
            ConfigurationFactory.ParseString(@"
            akka{
                actor{
                    serialize-messages = off
                }
            }
        ").WithFallback(TestConfigs.DefaultConfig));

        // Merges the SerializationSetup and BootstrapSetup together into a unified ActorSystemSetup class
        public static readonly ActorSystemSetup ActorSystemSettings = ActorSystemSetup.Create(SerializationSettings, Bootstrap);
        // </MergedSetup>

        [Fact]
        public void SerializationSetupShouldWorkAsExpected()
        {
            // <Verification>
            // consume the ActorSystemSetup
            using (var actorSystem = ActorSystem.Create("TestSerialization", ActorSystemSettings))
            {
                // implements IAppProtocol
                var ack = new Ack();

                // lookup the serializer configured by Akka.NET to manage Ack
                var foundSerializer = actorSystem.Serialization.FindSerializerFor(ack);

                // it's the custom serializer we specified in our SerializationSetup
                foundSerializer.Should().BeOfType<AppProtocolSerializer>();
            }
            // </Verification>
        }
        [Fact]
        public void UnsolvableSubstitutionWillThrowSample()
        {
            // <UnsolvableSubstitutionWillThrowSample>
            // This substitution will throw an exception because it is a required substitution,
            // and we can not resolve it.
            
            var hoconString = "from_environment = ${does-not-exist}";

            Assert.Throws<FormatException>(() =>
               {
                   Config config = hoconString;
               }).Message.Should().StartWith("Unresolved substitution");
            // </UnsolvableSubstitutionWillThrowSample>
        }
        [Fact]
        public void StringSubstitutionSample()
        {
            // <StringSubstitutionSample>
            // ${string_bar} will be substituted by 'bar' and concatenated with `foo` into `foobar`
            var hoconString = @"
            string_bar = bar
            string_foobar = foo${string_bar}
            ";
            var config = ConfigurationFactory.ParseString(hoconString); // This config uses ConfigurationFactory as a helper
            config.GetString("string_foobar").Should().Be("foobar");
            // </StringSubstitutionSample>
        }
        [Fact]
        public void ArraySubstitutionSample()
        {
            // <ArraySubstitutionSample>
            // ${a} will be substituted by the array [1, 2] and concatenated with [3, 4] to create [1, 2, 3, 4]
            var hoconString = @"
            a = [1,2]
            b = ${a} [3, 4]";
            Config config = hoconString; // This Config uses implicit conversion from string directly into a Config object
            (new[] { 1, 2, 3, 4 }).Should().BeEquivalentTo(config.GetIntList("b"));
            // </ArraySubstitutionSample>
        }
    }
}
