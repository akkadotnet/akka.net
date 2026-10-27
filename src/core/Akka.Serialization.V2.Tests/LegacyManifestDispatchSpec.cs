//-----------------------------------------------------------------------
// <copyright file="LegacyManifestDispatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Regression for https://github.com/akkadotnet/akka.net/issues/8784. Persistence plugins built against the
/// 1.5 contract (Akka.Persistence.Sql, Akka.Persistence.Redis and many third-party ones) pick the manifest they
/// store with <see cref="LegacyManifest"/>. A source-generated serializer must hit the
/// <see cref="SerializerWithStringManifest"/> branch, so the plugin stores the generated manifest rather than a
/// CLR type name the generated serializer refuses on recovery.
/// </summary>
public sealed class LegacyManifestDispatchSpec : AkkaSpec
{
    private static readonly Config SerializerConfig = ConfigurationFactory.ParseString(@"
        akka.actor {
            serializers {
                legacy-dispatch = ""Akka.Serialization.V2.Tests.LegacyDispatchSerializer, Akka.Serialization.V2.Tests""
            }
            serialization-bindings {
                ""Akka.Serialization.V2.Tests.ILegacyDispatchProtocol, Akka.Serialization.V2.Tests"" = legacy-dispatch
            }
        }");

    public LegacyManifestDispatchSpec(ITestOutputHelper output)
        : base(SerializerConfig, output)
    {
    }

    /// <summary>
    /// The exact manifest selection used by Akka.Persistence.Sql's journal and snapshot writers at 1.6.0-beta2.
    /// </summary>
    private static string LegacyManifest(Serializer serializer, object payload) => serializer switch
    {
        SerializerWithStringManifest stringManifest => stringManifest.Manifest(payload),
        { IncludeManifest: true } => payload.GetType().TypeQualifiedName(),
        _ => string.Empty
    };

    [Fact(DisplayName = "Should_store_generated_manifest_When_plugin_uses_legacy_SerializerWithStringManifest_dispatch")]
    public void Should_store_generated_manifest_When_plugin_uses_legacy_SerializerWithStringManifest_dispatch()
    {
        var payload = new LegacyDispatchPageChanged("hello");
        var serializer = Sys.Serialization.FindSerializerFor(payload);

        serializer.Should().BeOfType<LegacyDispatchSerializer>();

        var manifest = LegacyManifest(serializer, payload);
        manifest.Should().Be(LegacyDispatchSerializer.PageChangedManifest);
        serializer.Should().BeAssignableTo<SerializerWithStringManifest>();

        var bytes = serializer.ToBinary(payload);
        Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest)
            .Should().Be(payload);
    }

    [Fact(DisplayName = "Should_reject_CLR_type_name_manifest_When_generated_serializer_reads_it")]
    public void Should_reject_CLR_type_name_manifest_When_generated_serializer_reads_it()
    {
        // Pins what the bug wrote: the generated serializer does not treat a CLR type name as an alias for its
        // own manifest, so rows stored that way stay unreadable. This change stops them from being written.
        var payload = new LegacyDispatchPageChanged("hello");
        var serializer = Sys.Serialization.FindSerializerFor(payload);
        var bytes = serializer.ToBinary(payload);

        var act = () => Sys.Serialization.Deserialize(bytes, serializer.Identifier, payload.GetType().TypeQualifiedName());
        act.Should().Throw<System.Runtime.Serialization.SerializationException>();
    }
}

public interface ILegacyDispatchProtocol
{
}

[AkkaSerializable(Manifest = LegacyDispatchSerializer.PageChangedManifest)]
public sealed record LegacyDispatchPageChanged([property: AkkaField(0)] string Text) : ILegacyDispatchProtocol;

[AkkaSerializer<ILegacyDispatchProtocol>("legacy-dispatch", 120784)]
public sealed partial class LegacyDispatchSerializer : AkkaSerializer
{
    public const string PageChangedManifest = "legacy-dispatch-page-changed-v1";

    public static partial SerializerRegistration CreateRegistration();
}
