//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGeneratorDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class AkkaSerializerGeneratorDiagnosticsSpec
{
    [Fact(DisplayName = "Generator should fail compilation when nested value object lacks serialization definition")]
    public void Generator_should_fail_compilation_when_nested_value_object_lacks_serialization_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120501)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Inner Inner) : IProtocol;

            public sealed record Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id == "AKKASG007" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("must be annotated with [AkkaSerializable]", StringComparison.Ordinal));

        diagnostic.Should().NotBeNull();
    }

    [Fact(DisplayName = "Generator should fail compilation when deep nested value object lacks serialization definition")]
    public void Generator_should_fail_compilation_when_deep_nested_value_object_lacks_serialization_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120502)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Middle Middle) : IProtocol;

            [AkkaSerializable]
            public sealed record Middle([property: AkkaField(1)] Inner Inner);

            public sealed record Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id == "AKKASG007" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("must be annotated with [AkkaSerializable]", StringComparison.Ordinal));

        diagnostic.Should().NotBeNull();
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "AkkaSerializationGeneratorDiagnostics",
            new[] { syntaxTree },
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);

        return generatorDiagnostics.AddRange(updatedCompilation.GetDiagnostics());
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(AkkaSerializerAttribute).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies.Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}
