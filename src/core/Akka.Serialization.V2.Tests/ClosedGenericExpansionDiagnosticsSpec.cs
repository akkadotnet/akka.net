//-----------------------------------------------------------------------
// <copyright file="ClosedGenericExpansionDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Diagnostic-focused specs for Decision 18 (openspec/changes/messagepack-sourcegen-validation/design.md,
/// "Closed-Set Expansion And Adoption On The Serializer"): AKKASG040 (a <c>ManifestPrefix</c>
/// registration with no closed member set to expand), AKKASG041 (the one-owner rule), and AKKASG042
/// (the construction-count info diagnostic). Uses <see cref="GeneratorTestHarness"/> directly, the
/// same pattern <c>AkkaSerializerGeneratorDiagnosticsSpec</c> uses, rather than a real project
/// reference: these specs assert on diagnostics and generated text, not runtime round-tripping.
/// </summary>
public sealed class ClosedGenericExpansionDiagnosticsSpec
{
    [Fact(DisplayName = "Generator should report AKKASG040 when ManifestPrefix is set on a registration with no closed member set")]
    public void Generator_should_report_AKKASG040_for_a_concrete_class_type_argument()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ExpansionDiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "member-v1")]
            public sealed record Member([property: AkkaField(1)] string Value) : IProtocol;

            // Envelope<Member>'s type argument is a CONCRETE class: nothing to expand over.
            [AkkaSerializable]
            public sealed record Envelope<T>([property: AkkaField(1)] T Message);

            [AkkaSerializer<IProtocol>("sample", 140401)]
            [AkkaSerializable<Envelope<Member>>(ManifestPrefix = "env")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG040" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("SampleSerializer", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG040 when ManifestPrefix is set on a non-generic registration target")]
    public void Generator_should_report_AKKASG040_for_a_non_generic_target()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ExpansionDiagnosticSample2;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "plain-v1")]
            public sealed record Plain([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 140402)]
            [AkkaSerializable<Plain>(ManifestPrefix = "no-op")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG040" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG042 once per expanding ManifestPrefix registration, naming the construction count")]
    public void Generator_should_report_AKKASG042_with_the_construction_count()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ExpansionCountSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "a-v1")]
            public sealed record A([property: AkkaField(1)] int Value) : IProtocol;

            [AkkaSerializable(Manifest = "b-v1")]
            public sealed record B([property: AkkaField(1)] int Value) : IProtocol;

            [AkkaSerializable(Manifest = "c-v1")]
            public sealed record C([property: AkkaField(1)] int Value) : IProtocol;

            [AkkaSerializable]
            public sealed record Envelope<T>([property: AkkaField(1)] T Message);

            [AkkaSerializer<IProtocol>("sample", 140403)]
            [AkkaSerializable<Envelope<IProtocol>>(ManifestPrefix = "env")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG042" &&
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.GetMessage(null).Contains("3", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG041 when two serializers in the same compilation both adopt the same type")]
    public void Generator_should_report_AKKASG041_when_two_serializers_adopt_the_same_type()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace OneOwnerSample;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record Shared([property: AkkaField(1)] string Value);

            [AkkaSerializer<IProtocolA>("sample-a", 140404)]
            [AkkaSerializable<Shared>(Manifest = "shared-a-v1")]
            public sealed partial class SerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>("sample-b", 140405)]
            [AkkaSerializable<Shared>(Manifest = "shared-b-v1")]
            public sealed partial class SerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        var ownerConflicts = diagnostics.Where(diagnostic => diagnostic.Id == "AKKASG041").ToImmutableArray();

        ownerConflicts.Should().HaveCount(2, "the rule reports at BOTH owning serializers' own attributes");
        ownerConflicts.Should().OnlyContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ownerConflicts.Should().OnlyContain(diagnostic => diagnostic.GetMessage(null).Contains("Shared", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG041 when a type is adopted by one serializer and implements another serializer's protocol")]
    public void Generator_should_report_AKKASG041_when_adoption_collides_with_protocol_membership()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace OneOwnerSample2;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializable(Manifest = "member-v1")]
            public sealed record Member([property: AkkaField(1)] string Value) : IProtocolA;

            [AkkaSerializer<IProtocolA>("sample-a", 140406)]
            public sealed partial class SerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            // SerializerB adopts Member directly, even though it already belongs to SerializerA
            // through the protocol it implements.
            [AkkaSerializer<IProtocolB>("sample-b", 140407)]
            [AkkaSerializable<Member>(Manifest = "member-b-v1")]
            public sealed partial class SerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG041" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG012 when a derived manifest collides with another message's manifest")]
    public void Generator_should_report_AKKASG012_when_a_derived_manifest_collides()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ExpansionCollisionSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "b")]
            public sealed record First([property: AkkaField(1)] int Value) : IProtocol;

            [AkkaSerializable]
            public sealed record Envelope<T>([property: AkkaField(1)] T Message);

            [AkkaSerializable]
            public sealed record Box<T>([property: AkkaField(1)] T Content);

            [AkkaSerializer<IProtocol>("sample", 140408)]
            // Derives Envelope<First> = "env" + "/" + "b" = "env/b".
            [AkkaSerializable<Envelope<IProtocol>>(ManifestPrefix = "env")]
            // Collides with the derived manifest above -- deliberately, to prove a derived manifest
            // participates in the SAME AKKASG012 check as every hand-written one.
            [AkkaSerializable<Box<int>>(Manifest = "env/b")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG012" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("env/b", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG003 for a field typed as the serializer's own protocol interface")]
    public void Generator_should_not_report_AKKASG003_for_protocol_interface_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ProtocolUnionFieldSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "member-v1")]
            public sealed record Member([property: AkkaField(1)] int Value) : IProtocol;

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] IProtocol Inner) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 140409)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        return GeneratorTestHarness.Run(source).AllDiagnostics;
    }
}
