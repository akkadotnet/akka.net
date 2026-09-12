//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Placement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Akka.Serialization.V2.Generators;

// Decision 19 (openspec/changes/messagepack-sourcegen-validation/design.md): "making the compiler
// complain: the pit of success". A downstream compilation can see both its own message types AND,
// through metadata, an upstream serializer that already claims their protocol; an upstream
// compilation can see its own serializer AND, through CompilationFacts' referenced-assembly walk,
// whether any referenced assembly supplies its messages. Three of the four placement rules from
// the decision page's table become diagnostics here (the fourth, "unsupported generated serializer
// type", is a runtime exception-text improvement in AkkaSerializerGenerator.Emission.cs's
// GenerateManifest/GenerateSerializeDirect, since it has no compile-time site to report at).
public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// Reports Decision 19's three placement diagnostics, one whole-compilation, cross-serializer
    /// pass over the collected serializers and messages plus the cached <see cref="CompilationFacts"/>
    /// -- mirroring <see cref="ReportCrossSerializerDiagnostics"/> and <see cref="ReportProtocolCoverage"/>,
    /// the two existing outputs this one sits beside. Pure and symbol-free: every input is already a
    /// cached pipeline model, so this stage stays Cached across any edit none of them care about.
    /// Rules 1 and 3 both need nothing beyond <paramref name="facts"/> to know they have nothing to
    /// report: the overwhelmingly common case (no upstream serializer anywhere in the reference
    /// closure) never allocates <paramref name="messages"/>' own filtered/keyed projections at all --
    /// see <see cref="ReportProtocolOwnedUpstream"/>'s own early exit.
    /// </summary>
    private static void ReportPlacementDiagnostics(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages,
        CompilationFacts facts,
        LocationBag locations)
    {
        ReportProtocolOwnedUpstream(context, messages, serializers, facts, locations);
        ReportSerializerHasNoMessages(context, serializers, facts, locations);
        ReportDuplicateProtocolBindingCrossAssembly(context, serializers, facts, locations);
    }

    /// <summary>
    /// Rule 1: a type declared in THIS compilation implements a protocol an UPSTREAM serializer
    /// already binds. Scoped to <c>[AkkaSerializable]</c>-marked local messages -- an unmarked
    /// implementor of an upstream protocol is a real gap too, but reporting it needs its own
    /// whole-compilation walk and its own design decision about severity/site, which design.md's
    /// Decision 19 text does not specify; left for a follow-up (see this file's own design.md
    /// addendum). Exempt when a LOCAL serializer also binds the same protocol: that shape is Rule
    /// 3's (AKKASG031 extended), not this one's. Bails out before touching <paramref name="messages"/>
    /// or <paramref name="serializers"/> at all when there is no upstream serializer anywhere in the
    /// reference closure (<see cref="CompilationFacts.UpstreamSerializerBindingsByProtocol"/> empty)
    /// -- the overwhelmingly common case, and the one every edit re-checks (this stage's own combined
    /// input recomputes whenever <paramref name="facts"/> itself does).
    /// </summary>
    private static void ReportProtocolOwnedUpstream(
        SourceProductionContext context,
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<SerializerInfo?> serializers,
        CompilationFacts facts,
        LocationBag locations)
    {
        if (facts.UpstreamSerializerBindingsByProtocol.IsEmpty)
            return;

        var localProtocolKeys = new HashSet<TypeKey>();
        foreach (var serializer in serializers)
        {
            if (serializer != null && serializer.ProtocolTypeFullName.Length > 0)
                localProtocolKeys.Add(serializer.ProtocolTypeKey);
        }

        foreach (var message in messages)
        {
            if (message == null)
                continue;


            if (message.IsGenericDefinition)
                continue;

            foreach (var pair in facts.UpstreamSerializerBindingsByProtocol)
            {
                var protocolKey = pair.Key;
                if (localProtocolKeys.Contains(protocolKey))
                    continue;

                var protocolFullName = protocolKey.DisplayName ?? string.Empty;
                if (protocolFullName.Length == 0 || !message.Protocols.Contains(protocolFullName))
                    continue;

                var bindingText = DescribeUpstreamBindings(pair.Value);
                var spec = new DiagnosticSpec(DiagnosticKey.ProtocolOwnedUpstream, new LocationKey(message.Key, string.Empty),
                    ToDisplayName(message.FullyQualifiedName), ToDisplayName(protocolFullName), bindingText);
                context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(spec, locations));
            }
        }
    }

    /// <summary>
    /// Rule 2: a serializer with no messages anywhere this compilation can see -- locally, in a
    /// referenced assembly, or through a registration of its own -- and therefore very likely
    /// misplaced (its messages may live in an assembly that depends on this one, which it can never
    /// see). Advisory only: a message assembly whose serializer lives in a host below it is a
    /// legitimate shape design.md's own Decision 19 text calls out, so this cannot be an error.
    /// </summary>
    private static void ReportSerializerHasNoMessages(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        CompilationFacts facts,
        LocationBag locations)
    {
        foreach (var serializer in serializers)
        {
            if (serializer == null || serializer.ProtocolTypeFullName.Length == 0)
                continue;

            if (!serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
                continue;

            var hasLocal = facts.LocalMarkedImplementorsByClosedSetKey.TryGetValue(serializer.ProtocolTypeKey, out var localImplementors) && !localImplementors.IsEmpty;
            if (hasLocal)
                continue;

            var hasReferenced = facts.ReferencedAssemblyImplementorsByProtocol.TryGetValue(serializer.ProtocolTypeKey, out var referencedImplementors) && !referencedImplementors.IsEmpty;
            if (hasReferenced)
                continue;

            var spec = new DiagnosticSpec(DiagnosticKey.SerializerHasNoMessages, new LocationKey(serializer.Key, string.Empty),
                serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName));
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(spec, locations));
        }
    }

    /// <summary>
    /// Rule 3: the cross-assembly extension of AKKASG031 (same id -- see <see cref="DuplicateProtocolBindingCrossAssembly"/>'s
    /// own doc comment). A LOCAL serializer's protocol is ALSO bound by a serializer declared in a
    /// referenced assembly: the runtime binding lookup can only route a value to one serializer, so
    /// the two collide. Reported at the local serializer's own attribute, naming the upstream
    /// binding(s); the LOCAL-only case (two serializers in the SAME compilation) stays
    /// <see cref="ComputeDuplicateProtocolBindings"/>'s existing, unchanged check.
    /// </summary>
    private static void ReportDuplicateProtocolBindingCrossAssembly(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        CompilationFacts facts,
        LocationBag locations)
    {
        if (facts.UpstreamSerializerBindingsByProtocol.IsEmpty)
            return;

        foreach (var serializer in serializers)
        {
            if (serializer == null || serializer.ProtocolTypeFullName.Length == 0)
                continue;

            if (!facts.UpstreamSerializerBindingsByProtocol.TryGetValue(serializer.ProtocolTypeKey, out var upstreamBindings) || upstreamBindings.IsEmpty)
                continue;

            var bindingText = DescribeUpstreamBindings(upstreamBindings);
            var spec = new DiagnosticSpec(DiagnosticKey.DuplicateProtocolBindingCrossAssembly, new LocationKey(serializer.Key, string.Empty),
                ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName, bindingText);
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(spec, locations));
        }
    }

    /// <summary>Renders "'Serializer' in assembly 'Name'", joined for more than one binding -- shared text for Rules 1 and 3.</summary>
    private static string DescribeUpstreamBindings(ImmutableArray<UpstreamSerializerBinding> bindings)
    {
        return string.Join(", ", bindings.Select(binding => $"'{ToDisplayName(binding.SerializerFullName)}' in assembly '{binding.AssemblyName}'"));
    }
}
