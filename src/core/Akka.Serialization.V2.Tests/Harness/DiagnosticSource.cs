//-----------------------------------------------------------------------
// <copyright file="DiagnosticSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Akka.Serialization.V2.Tests.Harness;

/// <summary>
/// Shared source template for the diagnostics specs that probe one message field type at a time: a
/// serializer over <c>IProtocol</c>, a message <c>Outer</c> whose constructor is the caller's field list,
/// a <c>Foreign</c> type with a ready-made formatter, and a <c>long</c>-backed enum.
/// </summary>
internal static class DiagnosticSource
{
    internal const string Header = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Akka.Actor;
        using Akka.Serialization.V2;
        using MessagePack;

        namespace DiagnosticSample;

        public interface IProtocol
        {
        }

        public sealed record Foreign(string Value);

        public enum WideEnum : long
        {
            A = 1
        }

        public sealed class ForeignFormatter : IAkkaMessagePackFormatter<Foreign>
        {
            public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
            public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
            public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
        }

        """;

    internal const string ForeignFormatterAttribute = "[AkkaSerializerFormatter<Foreign, ForeignFormatter>]";

    private static string Source(string serializerAttributes, string messageBody)
        => Header + $$"""
            [AkkaSerializer<IProtocol>("sample", 199001)]
            {{serializerAttributes}}
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer({{messageBody}}) : IProtocol;
            """;

    /// <summary>Runs the generator over <c>Outer(<paramref name="messageBody"/>)</c> and returns its error diagnostics plus the whole result.</summary>
    internal static RunOutcome Run(string serializerAttributes, string messageBody)
    {
        var result = GeneratorTestHarness.Run(Source(serializerAttributes, messageBody));
        return new RunOutcome(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(), result);
    }

    internal readonly record struct RunOutcome(Diagnostic[] Errors, GeneratorRunResult Result);
}
