//-----------------------------------------------------------------------
// <copyright file="InferredUnionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization.V2.Tests.Harness;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Which property types the generator turns into a union with no attribute, and which it leaves
/// alone. A union base is an interface, an abstract class, or a closed hierarchy (a C# 15
/// <c>closed</c> type, or a class whose constructors are all private and which has nested derived
/// types); its members are the <c>[AkkaSerializable]</c> types that derive from or implement it.
/// Collection-like and framework types, formatter targets, and ordinary concrete classes are not
/// union bases. Closed records (<see cref="UnionCompletenessDiagnosticsSpec"/>) and cross-assembly
/// discovery (<see cref="UnionBaseDiscoverySpec"/>) are covered in their own specs.
/// </summary>
public sealed class InferredUnionSpec : AkkaSpec
{
    public InferredUnionSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
    {
    }

    private const string SerializerSource = """

        public interface IDrawing { }

        [AkkaSerializer<IDrawing>("drawing", 160001)]
        public sealed partial class DrawingSerializer : AkkaSerializer
        { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
        """;

    [Fact(DisplayName = "Should_RoundTripEveryCase_When_FieldIsAbstractClassWithNoAttribute")]
    public void Should_RoundTripEveryCase_When_FieldIsAbstractClassWithNoAttribute()
    {
        // An open abstract base: public constructor, cases declared at top level.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.Abstract;

            public abstract record Shape;

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

            [AkkaSerializable(Manifest = "square")]
            public sealed record Square([property: AkkaField(1)] double Side) : Shape;

            [AkkaSerializable(Manifest = "drawn")]
            public sealed record Drawn([property: AkkaField(1)] Shape Shape, [property: AkkaField(2)] Shape? Shadow) : IDrawing;

            public static class Samples
            {
                public static object[] All() => [new Drawn(new Circle(1), null), new Drawn(new Square(2), new Circle(3))];
            }
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Inferred.Abstract.DrawingSerializer");
        foreach (var original in (object[])Invoke(assembly, "Inferred.Abstract.Samples", "All")!)
            serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original)).Should().Be(original);
    }

    [Fact(DisplayName = "Should_NotInferUnion_When_ConcreteBaseHasOnlyPrivateConstructorsAndNestedCases")]
    public void Should_NotInferUnion_When_ConcreteBaseHasOnlyPrivateConstructorsAndNestedCases()
    {
        // A concrete base can always hand out instances of itself -- here Coverage.None -- and no
        // union of its subclasses could write one. So it is not inferred; the field keeps its
        // AKKASG007 build error instead of building clean and failing on the first write of
        // Coverage.None. Making the base abstract (or C# 15 closed) is the way to get a union.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.PrivateCtor;

            public record Coverage
            {
                private Coverage() { }

                public static readonly Coverage None = new();

                [AkkaSerializable(Manifest = "paid")]
                public sealed record Paid([property: AkkaField(1)] long InvoiceNumber) : Coverage;

                [AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
                public sealed record Trial : Coverage;
            }

            [AkkaSerializable(Manifest = "covered")]
            public sealed record Covered([property: AkkaField(1)] Coverage Coverage) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG007");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019" || d.Id == "AKKASG048");
    }

    [Fact(DisplayName = "Should_NotInferUnion_When_ConcreteClassHasPrivateConstructorButNoNestedSubclass")]
    public void Should_NotInferUnion_When_ConcreteClassHasPrivateConstructorButNoNestedSubclass()
    {
        // The value-object/factory pattern: a private constructor guards creation, not
        // subclassing. A concrete class is never inferred, so it keeps the ordinary "nested type
        // is not [AkkaSerializable]" error instead of becoming an empty union.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.ValueObject;

            public class Email
            {
                private Email(string value) => Value = value;

                public string Value { get; }

                public static Email Create(string value) => new(value);
            }

            [AkkaSerializable(Manifest = "contact")]
            public sealed record Contact([property: AkkaField(1)] Email Email) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG007");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019" || d.Id == "AKKASG048");
    }

    [Theory(DisplayName = "Should_ReportAKKASG003_NotInferUnion_When_FieldIsCollectionOrFrameworkInterface")]
    [InlineData("IEnumerable<Circle>")]
    [InlineData("ISet<Circle>")]
    [InlineData("ICircleBag")]
    [InlineData("IComparable")]
    [InlineData("IDisposable")]
    [InlineData("Akka.Actor.ICanTell")]
    public void Should_ReportAKKASG003_NotInferUnion_When_FieldIsCollectionOrFrameworkInterface(string fieldType)
    {
        // A collection-like interface (anything implementing IEnumerable, including a user-declared
        // one), a System-namespace type, or a type from Akka.NET's own core assembly is never a
        // closed set of the user's own messages.
        var source = $$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using Akka.Serialization.V2;

            namespace Inferred.NotUnion;

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius);

            public interface ICircleBag : IEnumerable<Circle> { }

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] {{fieldType}} Value) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG003");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019" || d.Id == "AKKASG048");
    }

    [Fact(DisplayName = "Should_NotInferUnion_When_FieldIsAkkaCoreAbstractClass")]
    public void Should_NotInferUnion_When_FieldIsAkkaCoreAbstractClass()
    {
        // Akka.Actor.ActorPath is abstract, but it is Akka.NET's own type, not a closed set of the
        // user's messages: it keeps its "needs a formatter or [AkkaSerializable]" error.
        var source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.AkkaCore;

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] Akka.Actor.ActorPath Path) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG007");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019" || d.Id == "AKKASG048");
    }

    [Fact(DisplayName = "Should_UseFormatter_NotInferUnion_When_FormatterIsRegisteredForInterface")]
    public void Should_UseFormatter_NotInferUnion_When_FormatterIsRegisteredForInterface()
    {
        // A formatter registered for the interface claims every field of that type, exactly as it
        // does for any other type; union discovery never runs for it.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;
            using MessagePack;

            namespace Inferred.Formatter;

            public interface IShape { }

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : IShape;

            public sealed class ShapeFormatter : IAkkaMessagePackFormatter<IShape>
            {
                public void Write(ref MessagePackWriter writer, IShape value) => writer.Write(((Circle)value).Radius);
                public IShape Read(ref MessagePackReader reader) => new Circle(reader.ReadDouble());
                public int SizeOf(IShape value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            public interface IFormatted { }

            [AkkaSerializable(Manifest = "drawn")]
            public sealed record Drawn([property: AkkaField(1)] IShape Shape) : IFormatted;

            [AkkaSerializer<IFormatted>("formatted", 160002)]
            [AkkaSerializerFormatter<IShape, ShapeFormatter>]
            public sealed partial class FormattedSerializer : AkkaSerializer
            { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }

            public static class Samples
            {
                public static object One() => new Drawn(new Circle(4));
            }
            """;

        var result = GeneratorTestHarness.Run(source);
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("ShapeFormatter").And.NotContain("Union_IShape");

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Inferred.Formatter.FormattedSerializer");
        var original = Invoke(assembly, "Inferred.Formatter.Samples", "One")!;
        serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original)).Should().Be(original);
    }

    [Fact(DisplayName = "Should_ReportAKKASG029Only_When_ProtocolInterfaceFieldHasUnmarkedImplementor")]
    public void Should_ReportAKKASG029Only_When_ProtocolInterfaceFieldHasUnmarkedImplementor()
    {
        // A field typed as the serializer's own protocol is a union over the protocol set. Its
        // unmarked implementor is a protocol-coverage problem (AKKASG029), reported once, not a
        // second time as a union completeness error.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.Protocol;

            [AkkaSerializable(Manifest = "batch")]
            public sealed record Batch([property: AkkaField(1)] IDrawing Inner) : IDrawing;

            [AkkaSerializable(Manifest = "line")]
            public sealed record Line([property: AkkaField(1)] double Length) : IDrawing;

            public sealed record Forgotten(double Length) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG029" && d.GetMessage(null).Contains("Forgotten"));
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG048");
    }

    [Fact(DisplayName = "Should_ExpandPerImplementor_When_ManifestPrefixTypeArgumentIsNonProtocolInterface")]
    public void Should_ExpandPerImplementor_When_ManifestPrefixTypeArgumentIsNonProtocolInterface()
    {
        // IShape is not the protocol and carries no attribute: as an interface, it is a closed set
        // of its [AkkaSerializable] implementors, so ManifestPrefix expands over them.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.Prefix;

            public interface IShape { }

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : IShape;

            [AkkaSerializable(Manifest = "square")]
            public sealed record Square([property: AkkaField(1)] double Side) : IShape;

            [AkkaSerializable]
            public sealed record Boxed<T>([property: AkkaField(1)] T Shape) : IBoxes;

            public interface IBoxes { }

            [AkkaSerializer<IBoxes>("boxes", 160003)]
            [AkkaSerializerIncludes<Boxed<IShape>>(ManifestPrefix = "box")]
            public sealed partial class BoxSerializer : AkkaSerializer
            { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
            """;

        var result = GeneratorTestHarness.Run(source);

        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG040");
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("\"box/circle\"").And.Contain("\"box/square\"");
    }

    [Fact(DisplayName = "Should_ReportAKKASG019WithFix_When_InterfaceHasNoSerializableImplementor")]
    public void Should_ReportAKKASG019WithFix_When_InterfaceHasNoSerializableImplementor()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Inferred.Empty;

            public interface IShape { }

            [AkkaSerializable(Manifest = "drawn")]
            public sealed record Drawn([property: AkkaField(1)] IShape Shape) : IDrawing;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        var error = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG019").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage(null).Should().Contain("Inferred.Empty.IShape")
            .And.Contain("mark its implementations [AkkaSerializable] with a Manifest, or type the property as object");
        error.Location.Kind.Should().NotBe(LocationKind.None);
    }

    private Akka.Serialization.SerializerWithStringManifest CreateSerializer(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        return (Akka.Serialization.SerializerWithStringManifest)Activator.CreateInstance(type, (ExtendedActorSystem)Sys)!;
    }

    private static object? Invoke(Assembly assembly, string typeName, string method, params object[] args)
        => assembly.GetType(typeName, throwOnError: true)!.GetMethod(method)!.Invoke(null, args);
}
