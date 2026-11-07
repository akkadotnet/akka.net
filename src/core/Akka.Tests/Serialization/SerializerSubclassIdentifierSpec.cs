//-----------------------------------------------------------------------
// <copyright file="SerializerSubclassIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// A public, unsealed built-in serializer's <c>Identifier</c> override guards on <c>GetType()</c>, so a user
    /// subclass registered under its own HOCON id keeps resolving that id from HOCON instead of inheriting the
    /// built-in's hardcoded constant.
    /// </summary>
    public class SerializerSubclassIdentifierSpec
    {
        public sealed class MyJsonSerializer : NewtonSoftJsonSerializer
        {
            public MyJsonSerializer(ExtendedActorSystem system) : base(system)
            {
            }
        }

        [Fact(DisplayName = "Should_keep_resolving_its_own_HOCON_id_When_a_user_subclasses_a_builtin_serializer")]
        public void Should_keep_resolving_its_own_HOCON_id_When_a_user_subclasses_a_builtin_serializer()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-identifiers {
                    ""Akka.Tests.Serialization.SerializerSubclassIdentifierSpec+MyJsonSerializer, Akka.Tests"" = 100
                }");

            using var system = ActorSystem.Create(nameof(SerializerSubclassIdentifierSpec), config);
            var extendedSystem = (ExtendedActorSystem)system;

            new MyJsonSerializer(extendedSystem).Identifier.Should().Be(100);
            new NewtonSoftJsonSerializer(extendedSystem).Identifier.Should().Be(1);
        }
    }
}
