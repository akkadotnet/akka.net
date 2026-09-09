//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGeneratorDiagnosticLocationsSpec.cs" company="Akka.NET Project">
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
/// S6 "locations": every diagnostic this generator reports must carry a real
/// <see cref="Location"/>, not <see cref="Location.None"/> (see the location rule in
/// AkkaSerializerGenerator.Locations.cs). <see cref="AkkaSerializerGeneratorDiagnosticsSpec"/> and
/// <see cref="CrossAssemblyBaselineSpec"/> already cover diagnostic id/message text end to end; this
/// spec adds the ONE thing they do not check -- where each diagnostic points. It runs a small corpus
/// of fixtures, one per diagnostic family, and checks two things for each: EVERY generator diagnostic
/// has a real location, and a representative sample resolves to the EXACT expected source line.
/// </summary>
public sealed class AkkaSerializerGeneratorDiagnosticLocationsSpec
{
    [Theory(DisplayName = "Every diagnostic in the location-test corpus should report a real Location, not Location.None")]
    [MemberData(nameof(LocationCorpus))]
    public void Diagnostics_should_never_report_Location_None(string _, string source, string expectedId)
    {
        var diagnostics = GeneratorTestHarness.Run(source).GeneratorDiagnostics;

        var matching = diagnostics.Where(d => d.Id == expectedId).ToImmutableArray();
        matching.Should().NotBeEmpty($"the fixture should have produced at least one {expectedId} diagnostic");

        foreach (var diagnostic in matching)
            diagnostic.Location.Should().NotBe(Location.None, $"{expectedId} must report a real location, not Location.None");
    }

    public static TheoryData<string, string, string> LocationCorpus()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (name, source, id) in Corpus())
            data.Add(name, source, id);

        return data;
    }

    private static (string Name, string Source, string DiagnosticId)[] Corpus() => new[]
    {
        ("field-level (AKKASG003)", FieldLevelSource, "AKKASG003"),
        ("type-level (AKKASG006)", TypeLevelSource, "AKKASG006"),
        ("serializer-level (AKKASG002)", SerializerLevelSource, "AKKASG002"),
        ("formatter (AKKASG008)", FormatterSource, "AKKASG008"),
        ("closed generic registration (AKKASG020)", ClosedGenericSource, "AKKASG020"),
        ("closed generic missing fields (AKKASG004)", ClosedGenericMissingFieldsSource, "AKKASG004"),
        ("union member (AKKASG015)", UnionMemberSource, "AKKASG015"),
        ("duplicate field index (AKKASG005)", DuplicateFieldIndexSource, "AKKASG005"),
    };

    // ------------------------------------------------------------------------------------------
    // Field-level: the offending [AkkaField] property's own location.
    // ------------------------------------------------------------------------------------------
    private const string FieldLevelSource = """
        #nullable enable
        using System;
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.FieldLevel;

        public interface IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("field-level", 500001)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed class Outer : IProtocol
        {
            [AkkaField(1)]
            public Action Callback { get; set; } = () => { };
        }
        """;

    [Fact(DisplayName = "Field-level AKKASG003 should report at the offending property's own line")]
    public void Field_level_diagnostic_should_report_at_property_line()
    {
        var diagnostics = GeneratorTestHarness.Run(FieldLevelSource).GeneratorDiagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "AKKASG003").Subject;
        var expectedLine = LineIndexOf(FieldLevelSource, "public Action Callback");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(expectedLine);
    }

    // ------------------------------------------------------------------------------------------
    // Type-level: the message type's own declaration line (its identifier).
    // ------------------------------------------------------------------------------------------
    private const string TypeLevelSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.TypeLevel;

        public interface IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("type-level", 500002)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable]
        public sealed record NoManifest([property: AkkaField(1)] string Value) : IProtocol;
        """;

    [Fact(DisplayName = "Type-level AKKASG006 should report at the message type's own declaration line")]
    public void Type_level_diagnostic_should_report_at_message_declaration_line()
    {
        var diagnostics = GeneratorTestHarness.Run(TypeLevelSource).GeneratorDiagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "AKKASG006").Subject;
        var expectedLine = LineIndexOf(TypeLevelSource, "public sealed record NoManifest");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(expectedLine);
    }

    // ------------------------------------------------------------------------------------------
    // Serializer-level: the [AkkaSerializer<TProtocol>] attribute application, not the class name.
    // ------------------------------------------------------------------------------------------
    private const string SerializerLevelSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.SerializerLevel;

        public interface IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("serializer-level", 0)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
        """;

    [Fact(DisplayName = "Serializer-level AKKASG002 should report at the serializer's own attribute line, not its class declaration")]
    public void Serializer_level_diagnostic_should_report_at_attribute_line()
    {
        var diagnostics = GeneratorTestHarness.Run(SerializerLevelSource).GeneratorDiagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "AKKASG002").Subject;
        var expectedLine = LineIndexOf(SerializerLevelSource, "[AkkaSerializer<IProtocol>");
        var classLine = LineIndexOf(SerializerLevelSource, "public sealed partial class SampleSerializer");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(expectedLine);
        expectedLine.Should().NotBe(classLine, "the attribute and the class declaration must sit on different lines for this assertion to be meaningful");
    }

    // ------------------------------------------------------------------------------------------
    // AKKASG029 protocol coverage: the serializer's own attribute (the unmarked implementor has no
    // local attributed site of its own -- see ValidateProtocolCoverage's doc comment).
    // ------------------------------------------------------------------------------------------
    private const string ProtocolCoverageSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.Coverage;

        public interface IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("coverage", 500003)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        public sealed record Unmarked(string Value) : IProtocol;
        """;

    [Fact(DisplayName = "AKKASG029 should report at the serializer's own attribute line, not the unmarked implementor")]
    public void Protocol_coverage_diagnostic_should_report_at_serializer_attribute_line()
    {
        var diagnostics = GeneratorTestHarness.Run(ProtocolCoverageSource).GeneratorDiagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "AKKASG029").Subject;
        diagnostic.Location.Should().NotBe(Location.None);

        var expectedLine = LineIndexOf(ProtocolCoverageSource, "[AkkaSerializer<IProtocol>");
        var unmarkedLine = LineIndexOf(ProtocolCoverageSource, "public sealed record Unmarked");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(expectedLine);
        expectedLine.Should().NotBe(unmarkedLine, "AKKASG029 must not point at the unmarked implementor itself");
    }

    // ------------------------------------------------------------------------------------------
    // Cross-assembly: the LOCAL referencing property, in THIS compilation -- never a location
    // inside the referenced assembly, which the generator never even parses (Decision 16). Money
    // deliberately carries NO [AkkaSerializable] here: since Decision 16, a referenced type that IS
    // [AkkaSerializable] and accessible resolves through metadata (CrossAssemblyBaselineSpec covers
    // that success path); this fixture keeps testing the LOCATION mechanism for the one failure mode
    // Decision 16 does not touch -- a referenced type with no schema this generator can read at all.
    // ------------------------------------------------------------------------------------------
    private const string CrossAssemblySourceA = """
        #nullable enable
        namespace LocationSample.CrossAssembly.AssemblyA;

        public sealed record Money(long Cents);
        """;

    private const string CrossAssemblySourceB = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;
        using LocationSample.CrossAssembly.AssemblyA;

        namespace LocationSample.CrossAssembly.AssemblyB;

        public interface IShop
        {
        }

        [AkkaSerializable(Manifest = "pay-v1")]
        public sealed record Pay([property: AkkaField(1)] Money Amount) : IShop;

        [AkkaSerializer<IShop>("shop", 500004)]
        public sealed partial class ShopSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    [Fact(DisplayName = "Cross-assembly AKKASG007 should report at the LOCAL referencing property in this compilation, never inside the referenced assembly")]
    public void Cross_assembly_diagnostic_should_report_at_local_referencing_property()
    {
        var assemblyA = GeneratorTestHarness.CompileToReference(CrossAssemblySourceA, "LocationSampleCrossAssemblyA");
        var result = GeneratorTestHarness.Run(CrossAssemblySourceB, assemblyA);

        var diagnostic = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG007").Subject;
        diagnostic.Location.Should().NotBe(Location.None);

        // A DiagnosticSpec's location is reconstructed from a captured LocationSpec via
        // Location.Create(filePath, span, linePositionSpan) -- an "external" location with no live
        // SourceTree attached (the tree that produced it is long gone by report time). GetLineSpan()
        // still resolves correctly from the captured path/positions, which is what a diagnostic
        // renderer (an IDE, or this assertion) actually reads.
        var lineSpan = diagnostic.Location.GetLineSpan();

        // The location's file path must be assembly B's own source file, never assembly A's --
        // proof this is genuinely the LOCAL reference site, not something borrowed from A's tree
        // (A's syntax tree is never even loaded by the generator; only its metadata is read).
        lineSpan.Path.Should().NotContain("LocationSampleCrossAssemblyA");

        var expectedLine = LineIndexOf(CrossAssemblySourceB, "public sealed record Pay(");
        lineSpan.StartLinePosition.Line.Should().Be(expectedLine);
    }

    // ------------------------------------------------------------------------------------------
    // Closed-generic construction, TYPE-LEVEL: a construction has no syntax of its own (only its
    // GENERIC DEFINITION does), so a type-level diagnostic on it (MissingFields here) must redirect
    // to that construction's own [AkkaSerializable<T>] registration attribute on the serializer --
    // see MessageTypeLocationKey's doc comment in AkkaSerializerGenerator.Locations.cs.
    // ------------------------------------------------------------------------------------------
    private const string ClosedGenericMissingFieldsSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.ClosedGenericMissingFields;

        public interface IProtocol
        {
        }

        [AkkaSerializable]
        public sealed class EmptyWrapper<T> : IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("closed-generic-missing-fields", 500009)]
        [AkkaSerializable<EmptyWrapper<int>>(Manifest = "empty-wrapper-v1")]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    [Fact(DisplayName = "AKKASG004 on a closed-generic construction should report at its own registration attribute, not the generic definition or the serializer's main attribute")]
    public void Closed_generic_type_level_diagnostic_should_report_at_registration_attribute_line()
    {
        var diagnostics = GeneratorTestHarness.Run(ClosedGenericMissingFieldsSource).GeneratorDiagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "AKKASG004").Subject;
        diagnostic.Location.Should().NotBe(Location.None);

        var expectedLine = LineIndexOf(ClosedGenericMissingFieldsSource, "[AkkaSerializable<EmptyWrapper<int>>");
        var definitionLine = LineIndexOf(ClosedGenericMissingFieldsSource, "public sealed class EmptyWrapper<T>");
        var serializerAttributeLine = LineIndexOf(ClosedGenericMissingFieldsSource, "[AkkaSerializer<IProtocol>");

        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(expectedLine);
        expectedLine.Should().NotBe(definitionLine, "the construction has no syntax of its own -- the generic definition's own declaration is the wrong site");
        expectedLine.Should().NotBe(serializerAttributeLine, "this diagnostic is about ONE registration, not the serializer as a whole");
    }

    // ------------------------------------------------------------------------------------------
    // Additional corpus fixtures feeding Diagnostics_should_never_report_Location_None above.
    // ------------------------------------------------------------------------------------------
    private const string FormatterSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;
        using MessagePack;

        namespace LocationSample.Formatter;

        public interface IProtocol
        {
        }

        public sealed record Foreign(string Value);

        public abstract class AbstractFormatter : IAkkaMessagePackFormatter<Foreign>
        {
            public abstract void Write(ref MessagePackWriter writer, Foreign value);
            public abstract Foreign Read(ref MessagePackReader reader);
            public abstract int SizeOf(Foreign value);
        }

        [AkkaSerializer<IProtocol>("formatter", 500005)]
        [AkkaSerializerFormatter<Foreign, AbstractFormatter>]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
        """;

    private const string ClosedGenericSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.ClosedGeneric;

        public interface IProtocol
        {
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;

        [AkkaSerializer<IProtocol>("closed-generic", 500006)]
        [AkkaSerializable<int>(Manifest = "int-v1")]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    private const string UnionMemberSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.UnionMember;

        public interface IProtocol
        {
        }

        public interface IEvent
        {
        }

        public sealed record NotSerializable(string Value) : IEvent;

        [AkkaSerializer<IProtocol>("union-member", 500007)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed record Outer(
            [property: AkkaField(1), AkkaUnion(typeof(NotSerializable))] IEvent Event) : IProtocol;
        """;

    private const string DuplicateFieldIndexSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace LocationSample.DuplicateFieldIndex;

        public interface IProtocol
        {
        }

        [AkkaSerializer<IProtocol>("dup-index", 500008)]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "dup-v1")]
        public sealed record DupIndex(
            [property: AkkaField(1)] string A,
            [property: AkkaField(1)] string B) : IProtocol;
        """;

    /// <summary>The zero-based line index of the first line containing <paramref name="needle"/>.</summary>
    private static int LineIndexOf(string source, string needle)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException($"'{needle}' was not found in the fixture source.");
    }
}
