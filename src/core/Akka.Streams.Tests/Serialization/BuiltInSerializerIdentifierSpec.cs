//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
// This test project already has a class named `StreamRefSerializer` (a spec, not a serializer) in this very
// namespace, so the real serializer type is referenced through this alias to avoid the clash.
using RealStreamRefSerializer = Akka.Streams.Serialization.StreamRefSerializer;

namespace Akka.Streams.Tests.Serialization
{
    /// <summary>
    /// Akka.Streams' built-in serializer used to read its <see cref="Serializer.Identifier"/> lazily from
    /// <c>akka.actor.serialization-identifiers</c>. It now declares it in code. This spec pins the code value to
    /// exactly what Akka.Streams' reference.conf still ships, so the two can't drift apart before the HOCON row
    /// is deleted in a later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        private static readonly Config ModuleConfig = ActorMaterializer.DefaultConfig();

        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output) : base(ModuleConfig, output)
        {
        }

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_StreamRefSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_StreamRefSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new RealStreamRefSerializer((ExtendedActorSystem)Sys);
            var expectedId = ModuleConfig
                .GetInt($"akka.actor.serialization-identifiers.\"{typeof(RealStreamRefSerializer).TypeQualifiedName()}\"");

            serializer.Identifier.Should().Be(30);
            serializer.Identifier.Should().Be(expectedId);
        }
    }
}
