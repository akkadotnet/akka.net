//-----------------------------------------------------------------------
// <copyright file="GeneratedMemberNamingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GeneratedMemberNamingSpec
{
    [Theory(DisplayName = "Should compile serializers when distinct message identities have the same simple name")]
    [InlineData("namespaces")]
    [InlineData("containing-types")]
    [InlineData("generic-arguments")]
    [InlineData("nested-fields")]
    public void Should_compile_when_distinct_types_share_simple_names(string scenario)
    {
        var source = """
            #nullable enable
            using Akka.Serialization.V2;
            namespace Sample
            {
                public interface IProtocol { }
                [AkkaSerializer<IProtocol>("naming", 120901)]
            """ + (scenario == "generic-arguments" ? """
                [AkkaSerializable<Wrapper<First.Payload>>(Manifest = "first-wrapper")]
                [AkkaSerializable<Wrapper<Second.Payload>>(Manifest = "second-wrapper")]
            """ : string.Empty) + """
                public sealed partial class SampleSerializer : AkkaSerializer
                {
                    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
                }
            }
            """ + (scenario switch
            {
                "namespaces" => """
                    namespace First
                    {
                        [AkkaSerializable(Manifest = "first")]
                        public sealed record LeaveChat([property: AkkaField(0)] int Value) : Sample.IProtocol;
                    }
                    namespace Second
                    {
                        [AkkaSerializable(Manifest = "second")]
                        public sealed record LeaveChat([property: AkkaField(0)] int Value) : Sample.IProtocol;
                    }
                    """,
                "containing-types" => """
                    namespace Sample
                    {
                        public static class Commands
                        {
                            [AkkaSerializable(Manifest = "command")]
                            public sealed record LeaveChat([property: AkkaField(0)] int Value) : IProtocol;
                        }
                        public static class Events
                        {
                            [AkkaSerializable(Manifest = "event")]
                            public sealed record LeaveChat([property: AkkaField(0)] int Value) : IProtocol;
                        }
                    }
                    """,
                "generic-arguments" => """
                    namespace Sample
                    {
                        [AkkaSerializable]
                        public sealed record Wrapper<T>([property: AkkaField(0)] T Value) : IProtocol;
                    }
                    namespace First
                    {
                        [AkkaSerializable]
                        public sealed record Payload([property: AkkaField(0)] int Value);
                    }
                    namespace Second
                    {
                        [AkkaSerializable]
                        public sealed record Payload([property: AkkaField(0)] string Value);
                    }
                    """,
                _ => """
                    namespace Sample
                    {
                        [AkkaSerializable(Manifest = "carrier")]
                        public sealed record Carrier(
                            [property: AkkaField(0)] First.Payload First,
                            [property: AkkaField(1)] Second.Payload Second) : IProtocol;
                    }
                    namespace First
                    {
                        [AkkaSerializable]
                        public sealed record Payload([property: AkkaField(0)] int Value);
                    }
                    namespace Second
                    {
                        [AkkaSerializable]
                        public sealed record Payload([property: AkkaField(0)] string Value);
                    }
                    """
            });

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratedSources.Keys.Should().Contain("SampleSerializer.AkkaSerialization.g.cs");
    }
}
