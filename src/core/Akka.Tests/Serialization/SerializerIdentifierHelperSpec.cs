//-----------------------------------------------------------------------
// <copyright file="SerializerIdentifierHelperSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Covers <see cref="SerializerIdentifierHelper.GetSerializerIdentifierFromConfig"/>, which matches the
    /// keys under <c>akka.actor.serialization-identifiers</c> against the name of the type in hand instead of
    /// resolving every key back into a <see cref="Type"/>.
    /// </summary>
    public class SerializerIdentifierHelperSpec
    {
        /// <summary>
        /// Stand-ins for serializer types: <see cref="SerializerIdentifierHelper"/> only ever formats the
        /// <see cref="Type"/> it is handed, so these do not have to be <see cref="Serializer"/>s.
        /// </summary>
        public sealed class BareSpelling
        {
        }

        public sealed class QualifiedSpelling
        {
        }

        public sealed class FullAssemblyIdentitySpelling
        {
        }

        public sealed class NoSpaceAfterCommaSpelling
        {
        }

        public sealed class LowerCaseAssemblySpelling
        {
        }

        public sealed class ExtraIdentityComponentsSpelling
        {
        }

        public sealed class Outer
        {
            public sealed class Nested
            {
            }
        }

        public sealed class Unconfigured
        {
        }

        /// <remarks>
        /// The <c>Version</c> below is deliberately 99.0.0.0 - a version this repository will never ship - so
        /// the spec keeps proving that the identity components are stripped rather than accidentally passing
        /// because the literal happened to match the real assembly version.
        /// </remarks>
        private const string Identifiers = @"
            akka.actor.serialization-identifiers {
                # the bare namespace-qualified spelling
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+BareSpelling"" = 9101

                # the assembly-qualified spelling, which is what core's own akka.conf ships
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+QualifiedSpelling, Akka.Tests"" = 9102

                # the full assembly identity, as Type.AssemblyQualifiedName prints it
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+FullAssemblyIdentitySpelling, Akka.Tests, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null"" = 9103

                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+Outer+Nested, Akka.Tests"" = 9104

                # no space after the comma - Type.GetType accepted this, so it still has to match
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+NoSpaceAfterCommaSpelling,Akka.Tests"" = 9105

                # assembly name in the wrong case - Type.GetType compared assembly names case-insensitively
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+LowerCaseAssemblySpelling, akka.tests"" = 9106

                # the rarer AssemblyName components, which are stripped along with the usual three
                ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+ExtraIdentityComponentsSpelling, Akka.Tests, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null, ProcessorArchitecture=MSIL, Retargetable=Yes"" = 9107

                # a serializer from a package that is configured but not referenced by this application:
                # Type.GetType cannot resolve it, and it must not take the other lookups down with it
                ""Some.Absent.Package.Serializer, Some.Absent.Package"" = 9199
            }";

        private static async Task WithSystem(Action<ExtendedActorSystem> body)
        {
            // Creating the system at all is half the assertion: Serialization..ctor reads Serializer.Identifier
            // for every serializer it registers, so the unresolvable key above used to make this throw.
            var system = (ExtendedActorSystem)ActorSystem.Create(
                nameof(SerializerIdentifierHelperSpec),
                ConfigurationFactory.ParseString(Identifiers));
            try
            {
                body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        public static TheoryData<Type, int> ConfiguredSpellings => new()
        {
            { typeof(BareSpelling), 9101 },
            { typeof(QualifiedSpelling), 9102 },
            { typeof(FullAssemblyIdentitySpelling), 9103 },
            { typeof(Outer.Nested), 9104 },
            { typeof(NoSpaceAfterCommaSpelling), 9105 },
            { typeof(LowerCaseAssemblySpelling), 9106 },
            { typeof(ExtraIdentityComponentsSpelling), 9107 },

            // the two serializers core's own akka.conf configures, alongside the unresolvable key
            { typeof(ByteArraySerializer), 4 },
            { typeof(NewtonSoftJsonSerializer), 1 }
        };

        [Theory(DisplayName = "GetSerializerIdentifierFromConfig should match every spelling of a key that Type.GetType used to resolve")]
        [MemberData(nameof(ConfiguredSpellings))]
        public async Task Should_match_the_configured_key_When_looking_up_a_serializer_id(Type type, int expectedId)
        {
            await WithSystem(system =>
                SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(type, system).Should().Be(expectedId));
        }

        /// <remarks>
        /// Behavior-preservation guard: a type with no entry in the block threw <see cref="ArgumentException"/>
        /// before this change and must keep doing so, message and all.
        /// </remarks>
        [Fact(DisplayName = "GetSerializerIdentifierFromConfig should throw when the type has no entry in the block")]
        public async Task Should_throw_ArgumentException_When_the_type_is_not_configured()
        {
            await WithSystem(system =>
            {
                var exception = Assert.Throws<ArgumentException>(
                    () => SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(typeof(Unconfigured), system));

                exception.Message.Should().Contain(typeof(Unconfigured).ToString());
                exception.Message.Should().Contain("akka.actor.serialization-identifiers");
            });
        }

        [Fact(DisplayName = "GetSerializerIdentifierFromConfig should prefer an assembly-qualified key over a bare one further up the block")]
        public async Task Should_prefer_the_assembly_qualified_key_When_a_bare_key_also_matches()
        {
            // Both keys name the same type. The bare one comes first on purpose: assembly-qualified keys are
            // matched across the whole block before any bare key is considered, so the exact one has to win no
            // matter where it sits.
            var config = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-identifiers {
                    ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+Unconfigured"" = 9201
                    ""Akka.Tests.Serialization.SerializerIdentifierHelperSpec+Unconfigured, Akka.Tests"" = 9202
                }");

            var system = (ExtendedActorSystem)ActorSystem.Create("serializer-id-precedence", config);
            try
            {
                SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(typeof(Unconfigured), system)
                    .Should().Be(9202);
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
