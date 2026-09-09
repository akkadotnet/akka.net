//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Emission.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.V2.Generators;

/// <summary>
/// The per-serializer view of the message table computed by <see cref="AkkaSerializerGenerator.ResolveSerializerMessages"/>:
/// every message with hand-written formatters swapped in (<see cref="ResolvedMessagesByType"/>),
/// which of those are top-level (dispatched directly by this serializer's Manifest/Serialize/
/// Deserialize switches), and which are reachable from a top-level message (and therefore need
/// generated Write/Read/SizeOf methods at all). Computed once per gate-passing serializer and shared
/// between validation (<see cref="AkkaSerializerGenerator.Validate"/>) and the cached
/// <see cref="AkkaSerializerGenerator.ResolvedSerializer"/> model (<see cref="AkkaSerializerGenerator.ResolveSerializer"/>)
/// so the two never see a different message table for the same input. Not itself a cached pipeline
/// model -- like <see cref="SerializerGate"/>, it lives at namespace scope instead of nested under
/// <see cref="AkkaSerializerGenerator"/>.
/// </summary>
internal readonly struct ResolvedSerializerMessages
{
    public ResolvedSerializerMessages(
        ImmutableArray<AkkaSerializerGenerator.MessageInfo> topLevelMessages,
        ImmutableArray<AkkaSerializerGenerator.MessageInfo> reachableMessages,
        ImmutableDictionary<string, AkkaSerializerGenerator.MessageInfo> resolvedMessagesByType)
    {
        TopLevelMessages = topLevelMessages;
        ReachableMessages = reachableMessages;
        ResolvedMessagesByType = resolvedMessagesByType;
    }

    public ImmutableArray<AkkaSerializerGenerator.MessageInfo> TopLevelMessages { get; }
    public ImmutableArray<AkkaSerializerGenerator.MessageInfo> ReachableMessages { get; }
    public ImmutableDictionary<string, AkkaSerializerGenerator.MessageInfo> ResolvedMessagesByType { get; }
}

public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// Every non-null message model, cast down from the raw collected array. Shared by
    /// <see cref="ReportCrossSerializerDiagnostics"/>, <see cref="ReportProtocolCoverage"/>, and the
    /// per-serializer <see cref="ResolveSerializer"/> step so the three never risk computing this
    /// projection differently.
    /// </summary>
    private static ImmutableArray<MessageInfo> ComputeDeclaredMessages(ImmutableArray<MessageInfo?> messages)
    {
        return messages
            .Where(message => message != null)
            .Cast<MessageInfo>()
            .ToImmutableArray();
    }

    /// <summary>
    /// Generic definitions are placeholders: never serialized, never top-level, never in the
    /// message dictionary (their arity-less key could even collide with a same-named non-generic
    /// type). They exist only for the AKKASG022/AKKASG037 checks.
    /// </summary>
    private static ImmutableArray<MessageInfo> ComputeGenericDefinitions(ImmutableArray<MessageInfo> declaredMessages)
    {
        return declaredMessages
            .Where(message => message.IsGenericDefinition)
            .ToImmutableArray();
    }

    /// <summary>
    /// The cross-serializer diagnostics that cannot be attached to any one serializer's
    /// <see cref="ResolvedSerializer"/> without either duplicating them (one copy per serializer) or
    /// picking an arbitrary "owner" serializer to carry them: AKKASG013 (duplicate serializer id),
    /// AKKASG031 (duplicate protocol binding), and AKKASG037 (Manifest ignored on a generic
    /// definition, which is not even serializer-scoped). Registered as its own
    /// <c>RegisterSourceOutput</c> over the collected serializers and messages, separate from
    /// <see cref="ResolveSerializer"/>'s per-serializer output, so each of these is reported EXACTLY
    /// ONCE regardless of how many serializers are declared -- the per-serializer output would
    /// otherwise need to single out one serializer as the "owner" to avoid reporting a duplicate-id
    /// pair once per serializer in the group. This mirrors the diagnostics-only shape
    /// <see cref="ReportProtocolCoverage"/> already used for AKKASG029.
    /// </summary>
    private static void ReportCrossSerializerDiagnostics(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages)
    {
        var duplicateSerializerIds = ComputeDuplicateSerializerIds(serializers);

        foreach (var duplicate in duplicateSerializerIds)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateSerializerId, Location.None, duplicate.Key, duplicate.Value));
        }

        // Same computation as duplicateSerializerIds above, grouped on the protocol interface
        // instead of the numeric id: two [AkkaSerializer] classes bound to the same protocol is
        // silent last-wins at runtime registration today (AKKASG031).
        var duplicateProtocolBindings = ComputeDuplicateProtocolBindings(serializers);

        foreach (var duplicate in duplicateProtocolBindings)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateProtocolBinding, Location.None, ToDisplayName(duplicate.Key), duplicate.Value));
        }

        var declaredMessages = ComputeDeclaredMessages(messages);

        // Advisory only (AKKASG037): a Manifest on a generic [AkkaSerializable] DEFINITION is
        // silently ignored -- the definition is never serialized directly (see ExtractMessage),
        // and every registered closed construction carries its own per-construction Manifest from
        // [AkkaSerializable<T>]. Reported once per definition, independent of any serializer.
        foreach (var definition in declaredMessages.Where(message => message.IsGenericDefinition && !string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ManifestIgnoredOnGenericDefinition, Location.None, ToDisplayName(definition.FullyQualifiedName), definition.Manifest));
        }
    }

    /// <summary>
    /// Resolves ONE serializer -- gate, message table, closed dispatch sets, union plan, and
    /// validation diagnostics -- into the cached, value-equatable <see cref="ResolvedSerializer"/>
    /// model. This is the per-serializer <c>Select</c> node of the pipeline
    /// (<see cref="TrackingNames.ResolvedSerializers"/> in <see cref="Initialize"/>): its input is
    /// this ONE serializer plus the FULL collected messages array (a message can only be judged
    /// top-level/reachable/duplicate against every other message), so an edit to any message still
    /// recomputes every serializer's resolve step -- but the resulting <see cref="ResolvedSerializer"/>
    /// for a serializer that does not own the edited message compares EQUAL to its previous run,
    /// which is what lets <see cref="EmitResolvedSerializer"/>'s <c>RegisterSourceOutput</c> skip
    /// re-emitting that serializer's file. <paramref name="duplicateSerializerIds"/>,
    /// <paramref name="duplicateProtocolBindings"/>, and <paramref name="genericDefinitions"/> are
    /// precomputed by the caller from the same collected arrays every other serializer's resolve
    /// step also sees, exactly as <see cref="ReportCrossSerializerDiagnostics"/> and
    /// <see cref="ReportProtocolCoverage"/> independently recompute them from the same source --
    /// see <see cref="EvaluateGate"/>'s doc comment.
    /// </summary>
    private static ResolvedSerializer ResolveSerializer(
        SerializerInfo serializer,
        ImmutableArray<MessageInfo> declaredMessages,
        ImmutableDictionary<int, string> duplicateSerializerIds,
        ImmutableDictionary<string, string> duplicateProtocolBindings,
        ImmutableArray<MessageInfo> genericDefinitions)
    {
        var gate = EvaluateGate(serializer, duplicateSerializerIds, duplicateProtocolBindings, genericDefinitions);
        if (!gate.IsEmittable)
            return ResolvedSerializer.NotEmittable(serializer, gate.Diagnostics);

        var resolved = ResolveSerializerMessages(serializer, declaredMessages);
        var validationDiagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        ValidateResolved(serializer, resolved, validationDiagnostics);

        var topLevelMessages = BuildClosedSet(resolved.TopLevelMessages);
        var usedFormatters = CollectUsedFormatters(resolved.ReachableMessages);

        // PlanUnionHelpers and ResolveClosedGenericRegistrations both look up against
        // resolved.ResolvedMessagesByType -- the FULL, whole-compilation dictionary
        // ResolveSerializerMessages builds from declaredMessages, exactly as validation already
        // does (see ValidateResolved). That full dictionary must NOT become the model's OWN stored
        // ResolvedMessagesByType, though: it carries every OTHER serializer's messages too, so a
        // serializer that adopts none of them would still see this model change shape whenever any
        // of them do -- exactly the cross-serializer poisoning ResolvedSerializer.Equals must avoid
        // (see BuildResolvedMessageTable's doc comment).
        var unionPlan = PlanUnionHelpers(resolved.ReachableMessages, resolved.ResolvedMessagesByType);
        var resolvedClosedGenericRegistrations = ResolveClosedGenericRegistrations(serializer.ClosedGenericRegistrations, resolved.ResolvedMessagesByType);
        var resolvedMessagesByType = BuildResolvedMessageTable(resolved.ReachableMessages);

        return ResolvedSerializer.Emittable(
            serializer,
            gate.Diagnostics,
            resolvedMessagesByType,
            topLevelMessages,
            resolved.ReachableMessages,
            resolvedClosedGenericRegistrations,
            usedFormatters,
            unionPlan,
            validationDiagnostics.ToImmutable());
    }

    /// <summary>
    /// The message table actually STORED on <see cref="ResolvedSerializer.ResolvedMessagesByType"/>:
    /// only this serializer's own reachable messages, keyed by type name -- deliberately narrower
    /// than the full, whole-compilation dictionary <see cref="ResolveSerializerMessages"/> builds
    /// (and validation still uses, unchanged) internally. A serializer's cached
    /// <see cref="ResolvedSerializer"/> must depend only on what actually shapes ITS OWN output; the
    /// full dictionary includes every other serializer's messages too, so storing it verbatim would
    /// make an untouched serializer's resolved model compare UNEQUAL whenever some unrelated
    /// serializer's message changed -- defeating the per-serializer caching this stage exists for.
    /// Every top-level message is already a member of <paramref name="reachableMessages"/> (see
    /// <see cref="CollectReachableMessages"/>, which seeds its walk from the top-level set), so
    /// nothing is lost by keying only off it.
    /// </summary>
    private static ImmutableDictionary<string, MessageInfo> BuildResolvedMessageTable(ImmutableArray<MessageInfo> reachableMessages)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, MessageInfo>(StringComparer.Ordinal);
        foreach (var message in reachableMessages)
            builder[message.FullyQualifiedName] = message;

        return builder.ToImmutable();
    }

    /// <summary>
    /// Reports a resolved serializer's own diagnostics (gate, then validation -- the same order
    /// <see cref="ResolveSerializer"/>'s predecessor, the single-output <c>EmitSerializers</c>, used
    /// to report them in) and emits its generated source, PURELY over the resolved model -- no
    /// <see cref="Compilation"/>, no other serializer's data. Registered directly on the
    /// <see cref="TrackingNames.ResolvedSerializers"/> values provider in <see cref="Initialize"/>,
    /// so the driver's own per-element caching (not any code here) is what makes an unrelated
    /// serializer's <c>AddSource</c> call Cached instead of re-running: this callback simply never
    /// executes for an element whose resolved model still compares equal to last time.
    /// </summary>
    private static void EmitResolvedSerializer(SourceProductionContext context, ResolvedSerializer resolved)
    {
        foreach (var gateDiagnostic in resolved.GateDiagnostics)
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(gateDiagnostic));

        if (!resolved.IsEmittable)
            return;

        foreach (var diagnostic in resolved.ValidationDiagnostics)
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(diagnostic));

        // Emission is suppressed by an ERROR-severity diagnostic only -- a Warning (e.g. AKKASG027)
        // or Info (e.g. AKKASG025) still lets the serializer generate normally, exactly as the old
        // isValid-returning Validate* chain already behaved (see ValidateResolved).
        if (resolved.ValidationDiagnostics.Any(diagnostic => DiagnosticRegistry.Resolve(diagnostic.Key).DefaultSeverity == DiagnosticSeverity.Error))
            return;

        context.AddSource(resolved.Serializer.ClassName + ".AkkaSerialization.g.cs", Generate(resolved));
    }

    /// <summary>
    /// Resolves one serializer's message table -- formatter substitution, top-level selection,
    /// reachability -- into the shape both validation (<see cref="ValidateResolved"/>/<see cref="Validate"/>)
    /// and code generation (<see cref="Generate"/>) need. Extracted so the two never risk seeing a
    /// different table for the same input: <see cref="ResolveSerializer"/> computes it once per
    /// gate-passing serializer and passes the SAME <see cref="ResolvedSerializerMessages"/> to both.
    /// </summary>
    private static ResolvedSerializerMessages ResolveSerializerMessages(SerializerInfo serializer, ImmutableArray<MessageInfo> declaredMessages)
    {
        var allMessages = declaredMessages
            .Where(message => !message.IsGenericDefinition)
            .Concat(serializer.ClosedGenericRegistrations
                .Where(registration => registration.Message != null)
                .Select(registration => registration.Message!))
            .ToImmutableArray();
        var allMessagesByType = allMessages.ToImmutableDictionary(message => message.FullyQualifiedName);
        var resolvedMessagesByType = ResolveMessages(allMessagesByType, serializer.Formatters);
        var topLevelMessages = allMessages
            .Where(message => serializer.ProtocolTypeFullName.Length > 0 && message.Protocols.Contains(serializer.ProtocolTypeFullName))
            .Select(message => resolvedMessagesByType[message.FullyQualifiedName])
            .ToImmutableArray();
        var reachableMessages = CollectReachableMessages(topLevelMessages, resolvedMessagesByType);

        return new ResolvedSerializerMessages(topLevelMessages, reachableMessages, resolvedMessagesByType);
    }

    private static ImmutableDictionary<string, MessageInfo> ResolveMessages(
        ImmutableDictionary<string, MessageInfo> allMessagesByType,
        ImmutableArray<FormatterInfo> formatters)
    {
        if (formatters.IsDefaultOrEmpty)
            return allMessagesByType;

        var formattersByTarget = new Dictionary<string, FormatterInfo>(StringComparer.Ordinal);
        foreach (var formatter in formatters)
            formattersByTarget[formatter.TargetTypeFullName] = formatter;

        var builder = ImmutableDictionary.CreateBuilder<string, MessageInfo>();
        foreach (var pair in allMessagesByType)
        {
            var message = pair.Value;
            var resolvedFields = ImmutableArray.CreateBuilder<FieldInfo>(message.Fields.Length);
            var changed = false;

            foreach (var field in message.Fields)
            {
                if (field.Mapping.Kind != FieldKind.EnvelopePayload &&
                    field.Mapping.TypeFullName.Length > 0 &&
                    formattersByTarget.TryGetValue(field.Mapping.TypeFullName, out var formatter))
                {
                    resolvedFields.Add(field.WithFormatter(new TypeMapping(FieldKind.Formatted, field.Mapping.TypeFullName), formatter));
                    changed = true;
                }
                else
                {
                    resolvedFields.Add(field);
                }
            }

            builder[pair.Key] = changed ? message.WithFields(resolvedFields.ToImmutable()) : message;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Renders one serializer's generated source PURELY from its <see cref="ResolvedSerializer"/> --
    /// no <see cref="Compilation"/>, no other serializer's data, and (since <see cref="ResolvedSerializer.UsedFormatters"/>
    /// and <see cref="ResolvedSerializer.UnionPlan"/> are already resolved) no re-derivation of
    /// anything <see cref="ResolveSerializer"/> already computed once.
    /// </summary>
    private static string Generate(ResolvedSerializer resolved)
    {
        var serializer = resolved.Serializer;
        var usedFormatters = resolved.UsedFormatters;

        var sb = new StringBuilder();
        var w = new CodeWriter(sb);
        w.Line("// <auto-generated />");
        w.Line("#nullable enable");
        w.Line("using System;");
        w.Line("using System.Buffers;");
        w.BlankLine();

        if (!string.IsNullOrEmpty(serializer.Namespace))
        {
            // The namespace is a dotted name chain, not a single identifier, so it takes the
            // deliberate raw path rather than CodeWriter.Identifier (which escapes one identifier).
            w.Raw("namespace ").Raw(serializer.Namespace).Line(";");
            w.BlankLine();
        }

        w.Raw(GetAccessibilityKeyword(serializer.DeclaredAccessibility)).Raw(" sealed partial class ").Identifier(serializer.ClassName).NewLine();
        using (w.Block())
        {
            GenerateFormatterFields(w, usedFormatters);
            w.Raw("public ").Identifier(serializer.ClassName).Line("(global::Akka.Actor.ExtendedActorSystem system) : base(system)");
            using (w.Block())
            {
                foreach (var formatter in usedFormatters)
                {
                    w.Identifier(GetFormatterFieldName(formatter)).Raw(" = new ").Type(TypeName.Global(formatter.FormatterTypeFullName)).Raw("(");
                    if (formatter.CtorKind == FormatterCtorKind.System)
                        w.Raw("system");
                    w.Line(");");
                }
            }

            w.BlankLine();
            w.Raw("public override int Identifier => ").Number(serializer.SerializerId).Line(";");
            w.BlankLine();
            GenerateRegistration(w, serializer);
            GenerateManifest(w, resolved.TopLevelMessages);
            GenerateSerialize(w);
            GenerateSerializeDirect(w, resolved.TopLevelMessages);
            GenerateDeserialize(w, resolved.TopLevelMessages);
            GenerateSizeHint(w, resolved.TopLevelMessages);
            GenerateCountingBufferWriter(w);

            // The union plan was already resolved to (signature -> helper name) once, in
            // ResolveSerializer; this dictionary is just that view, rebuilt here for the per-field
            // Write/Read/SizeOf call sites below (GenerateSizeField et al.) that look a helper up by
            // BuildUnionSignature(field) rather than iterating the plan.
            var unionHelpers = resolved.UnionPlan.Helpers.ToImmutableDictionary(helper => helper.Signature, helper => helper.HelperName, StringComparer.Ordinal);
            foreach (var message in resolved.ReachableMessages)
            {
                GenerateSizeMessage(w, message, unionHelpers);
                GenerateWriteMessage(w, message, unionHelpers);
                GenerateReadMessage(w, message, unionHelpers);
            }

            GenerateUnionHelpers(w, resolved.UnionPlan);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reduces an ordered <see cref="MessageInfo"/> array to a <see cref="ClosedSet"/> of (type name,
    /// manifest, method name) triples -- everything <see cref="GenerateManifest"/>,
    /// <see cref="GenerateSerializeDirect"/>, <see cref="GenerateDeserialize"/>, and
    /// <see cref="GenerateSizeHint"/> need for top-level dispatch, and nothing more (in particular,
    /// no <see cref="FieldInfo"/>, so the top-level set stays cheap to hold in the cached
    /// <see cref="ResolvedSerializer"/> model). Preserves <paramref name="messages"/>'s exact order --
    /// the same order the dispatch switches have always emitted in.
    /// </summary>
    private static ClosedSet BuildClosedSet(ImmutableArray<MessageInfo> messages)
    {
        var builder = ImmutableArray.CreateBuilder<ClosedSetMember>(messages.Length);
        foreach (var message in messages)
            builder.Add(new ClosedSetMember(message.FullyQualifiedName, message.Manifest, GetMessageMethodName(message)));

        return new ClosedSet(builder.ToImmutable());
    }

    /// <summary>
    /// Rewrites each closed-generic registration's <see cref="ClosedGenericRegistrationInfo.Message"/>
    /// to the FORMATTER-RESOLVED version of that message from <paramref name="resolvedMessagesByType"/>
    /// (see <see cref="ResolveMessages"/>) -- a registration's message, as extracted, predates
    /// per-serializer formatter substitution, exactly like every other message
    /// <see cref="ResolveSerializerMessages"/> folds in. An invalid registration
    /// (<see cref="ClosedGenericRegistrationInfo.Message"/> is null, or its type never made it into
    /// the resolved table) passes through unchanged -- AKKASG020 already gates that case in
    /// <see cref="EvaluateGate"/>, so this never actually happens for an emittable serializer, but a
    /// bare pass-through is simpler than asserting it here too.
    /// </summary>
    private static ImmutableArray<ClosedGenericRegistrationInfo> ResolveClosedGenericRegistrations(
        ImmutableArray<ClosedGenericRegistrationInfo> registrations,
        ImmutableDictionary<string, MessageInfo> resolvedMessagesByType)
    {
        if (registrations.IsDefaultOrEmpty)
            return ImmutableArray<ClosedGenericRegistrationInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<ClosedGenericRegistrationInfo>(registrations.Length);
        foreach (var registration in registrations)
        {
            if (registration.Message != null && resolvedMessagesByType.TryGetValue(registration.Message.FullyQualifiedName, out var resolvedMessage))
                builder.Add(new ClosedGenericRegistrationInfo(registration.TargetDisplayName, resolvedMessage));
            else
                builder.Add(registration);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<FormatterInfo> CollectUsedFormatters(ImmutableArray<MessageInfo> reachableMessages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var used = ImmutableArray.CreateBuilder<FormatterInfo>();
        foreach (var message in reachableMessages)
        {
            foreach (var field in message.Fields)
            {
                if (field.Mapping.Kind == FieldKind.Formatted && field.Formatter is { } formatter && seen.Add(formatter.TargetTypeFullName))
                    used.Add(formatter);
            }
        }

        if (used.Count == 0)
            return ImmutableArray<FormatterInfo>.Empty;

        return used.ToImmutable().Sort((a, b) => string.CompareOrdinal(a.TargetTypeFullName, b.TargetTypeFullName));
    }

    private static void GenerateFormatterFields(CodeWriter w, ImmutableArray<FormatterInfo> usedFormatters)
    {
        if (usedFormatters.Length == 0)
            return;

        foreach (var formatter in usedFormatters)
            w.Raw("private readonly ").Type(TypeName.Global(formatter.FormatterTypeFullName)).Raw(" ").Identifier(GetFormatterFieldName(formatter)).Line(";");

        w.BlankLine();
    }

    private static void GenerateRegistration(CodeWriter w, SerializerInfo serializer)
    {
        w.Line("public static partial global::Akka.Serialization.V2.SerializerRegistration CreateRegistration()");
        using (w.Block())
        {
            w.Raw("return global::Akka.Serialization.V2.SerializerRegistration.Create(").StringLiteral(serializer.Name).Line(",");
            using (w.Indented())
            {
                w.Raw("system => new ").Identifier(serializer.ClassName).Line("(system),");
                w.Line("global::System.Collections.Immutable.ImmutableHashSet.Create<global::System.Type>(");
                using (w.Indented())
                {
                    w.Raw("typeof(");
                    // An empty ProtocolTypeFullName (the [AkkaSerializer<T>] type argument was not a
                    // named type) is exempt from AKKASG033 and still reaches emission; it emits an
                    // empty typeof() exactly as the pre-CodeWriter emitter did -- broken generated
                    // code the USER build reports, never a generator crash.
                    if (serializer.ProtocolTypeFullName.Length > 0)
                        w.Type(TypeName.Global(serializer.ProtocolTypeFullName));
                    w.Line("))); ");
                }
            }
        }

        w.BlankLine();
    }

    private static void GenerateManifest(CodeWriter w, ClosedSet messages)
    {
        w.Line("public override string Manifest(object obj)");
        using (w.Block())
        {
            w.Line("return obj switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages.Members)
                    w.Type(TypeName.Global(message.TypeFullName)).Raw(" => ").StringLiteral(message.Manifest).Line(",");
                w.Line("_ => throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj))");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSerialize(CodeWriter w)
    {
        w.Line("public override int Serialize(object obj, IBufferWriter<byte> writer)");
        using (w.Block())
        {
            w.Line("var countingWriter = new AkkaGeneratedCountingBufferWriter(writer);");
            w.Line("var messagePackWriter = new global::MessagePack.MessagePackWriter(countingWriter);");
            w.Line("SerializeMessagePack(obj, ref messagePackWriter);");
            w.Line("messagePackWriter.Flush();");
            w.Line("return checked((int)countingWriter.BytesWritten);");
        }

        w.BlankLine();
    }

    private static void GenerateSerializeDirect(CodeWriter w, ClosedSet messages)
    {
        w.Line("private void SerializeMessagePack(object obj, ref global::MessagePack.MessagePackWriter writer)");
        using (w.Block())
        {
            w.Line("switch (obj)");
            using (var sw = w.Switch())
            {
                foreach (var message in messages.Members)
                {
                    using (sw.CaseTypePattern(TypeName.Global(message.TypeFullName), "message"))
                    {
                        w.Raw("Write").Identifier(message.MethodName).Line("(ref writer, message);");
                        w.Line("break;");
                    }
                }

                using (sw.Default())
                    w.Line("throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj));");
            }
        }

        w.BlankLine();
    }

    private static void GenerateDeserialize(CodeWriter w, ClosedSet messages)
    {
        w.Line("public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)");
        using (w.Block())
        {
            w.Line("var reader = new global::MessagePack.MessagePackReader(bytes);");
            w.Line("return manifest switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages.Members)
                    w.StringLiteral(message.Manifest).Raw(" => Read").Identifier(message.MethodName).Line("(ref reader),");
                w.Line("_ => throw new global::System.Runtime.Serialization.SerializationException($\"Unknown generated serializer manifest [{manifest}] for serializer [{GetType()}].\")");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSizeHint(CodeWriter w, ClosedSet messages)
    {
        w.Line("public override int SizeHint(object obj)");
        using (w.Block())
        {
            w.Line("return obj switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages.Members)
                    w.Type(TypeName.Global(message.TypeFullName)).Raw(" message => SizeOf").Identifier(message.MethodName).Line("(message),");
                w.Line("_ => global::Akka.Serialization.SerializerV2.UnknownSize");
            }
        }

        w.BlankLine();
    }

    private static void GenerateCountingBufferWriter(CodeWriter w)
    {
        w.Line("private sealed class AkkaGeneratedCountingBufferWriter : global::System.Buffers.IBufferWriter<byte>");
        using (w.Block())
        {
            w.Line("private readonly global::System.Buffers.IBufferWriter<byte> _inner;");
            w.BlankLine();
            w.Line("public AkkaGeneratedCountingBufferWriter(global::System.Buffers.IBufferWriter<byte> inner)");
            using (w.Block())
                w.Line("_inner = inner;");
            w.BlankLine();
            w.Raw("public long BytesWritten");
            using (w.InlineBraces())
                w.Raw("get; private set;");
            w.NewLine();
            w.BlankLine();
            w.Line("public void Advance(int count)");
            using (w.Block())
            {
                w.Line("_inner.Advance(count);");
                w.Line("BytesWritten += count;");
            }

            w.BlankLine();
            w.Line("public global::System.Memory<byte> GetMemory(int sizeHint = 0)");
            using (w.Block())
                w.Line("return _inner.GetMemory(sizeHint);");
            w.BlankLine();
            w.Line("public global::System.Span<byte> GetSpan(int sizeHint = 0)");
            using (w.Block())
                w.Line("return _inner.GetSpan(sizeHint);");
        }

        w.BlankLine();
    }

    private static void GenerateSizeMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, string> unionHelpers)
    {
        w.Raw("private int SizeOf").Identifier(GetMessageMethodName(message))
            .Raw("(").Type(TypeName.Global(message.FullyQualifiedName)).Line(" message)");
        using (w.Block())
        {
            w.Line("checked");
            using (w.Block())
            {
                w.Raw("var size = SizeOfMapHeader(").Number(message.Fields.Length).Line(");");
                var alloc = new NameAlloc();
                foreach (var field in message.Fields)
                    GenerateSizeField(w, unionHelpers, field, alloc);
                w.Line("return size;");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSizeField(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = ValueExpr.GeneratorOwned("message").Member(field.Name);
        var localName = Local.ForField(field.Name).WithSuffix("Size");
        w.Raw("size += SizeOfInt32(").Number(field.Index).Line(");");
        if (IsCollectionKind(field.Mapping.Kind))
        {
            var fieldSize = alloc.Next("size");

            // Only reachable for a Nullable<T>-wrapped VALUE-typed collection field (today: only
            // ImmutableArray<T>? -- every other collection kind is a reference type, so its
            // "nullable" comes from reference-nullability, not Nullable<T>, and IsNullableValueField
            // is always false for it). EmitSizeCollectionBody itself accesses members like .IsDefault
            // and .Length on `value` directly, which do not exist on the Nullable<T> WRAPPER -- so the
            // Nullable<T> layer must be peeled off (mirroring GenerateWriteField's identical
            // "if (value is null) ... else ...value.Value..." unwrap) before EmitSizeCollectionBody
            // ever sees the value.
            if (IsNullableValueField(field))
            {
                var unwrappedSize = alloc.Next("size");
                w.Raw("int ").Local(fieldSize).Line(";");
                w.Raw("if (").Value(value).Line(" is null)");
                using (w.Block())
                    w.Local(fieldSize).Line(" = SizeOfNil();");
                w.Line("else");
                using (w.Block())
                {
                    EmitSizeCollectionBody(w, field.Mapping, value.Member("Value"), unwrappedSize, alloc);
                    w.Local(fieldSize).Raw(" = ").Local(unwrappedSize).Line(";");
                }
            }
            else
            {
                EmitSizeCollectionBody(w, field.Mapping, value, fieldSize, alloc);
            }

            w.Raw("size += ").Local(fieldSize).Line(";");
            return;
        }

        if (TryEmitInlineSizeStatement(w, field, value))
            return;

        w.Raw("var ").Local(localName).Raw(" = ");
        GenerateSizeExpression(w, unionHelpers, field, value);
        w.Line(";");
        w.Raw("if (").Local(localName).Line(" < 0)");
        using (w.Indented())
            w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
        w.Raw("size += ").Local(localName).Line(";");
    }

    private static bool TryEmitInlineSizeStatement(CodeWriter w, FieldInfo field, ValueExpr value)
    {
        // Object, EnvelopePayload, and Union always route through the general
        // GenerateSizeExpression path below (they call a generated SizeOfXxx/SizeOfEnvelopePayload/
        // SizeOfUnion method, not a scalar MessagePackSizes helper) -- including when the field is a
        // nullable [AkkaSerializable] struct, which would otherwise match IsNullableValueField below
        // and get an inline scalar expression that EmitScalarSizeExpression cannot produce for
        // FieldKind.Object. Union sizes can also be UnknownSize and need the < 0 guard.
        if (field.Mapping.Kind is FieldKind.Formatted or FieldKind.Object or FieldKind.EnvelopePayload or FieldKind.Union)
            return false;

        if (IsNullableValueField(field))
        {
            w.Raw("size += ").Value(value).Raw(" is null ? SizeOfNil() : ");
            EmitScalarSizeExpression(w, field.Mapping, value.Member("Value"));
            w.Line(";");
            return true;
        }

        w.Raw("size += ");
        EmitScalarSizeExpression(w, field.Mapping, value);
        w.Line(";");
        return true;
    }

    private static void GenerateSizeExpression(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, ValueExpr value)
    {
        switch (field.Mapping.Kind)
        {
            case FieldKind.EnvelopePayload:
                w.Raw("SizeOfEnvelopePayload(").Value(value).Raw(")");
                break;
            case FieldKind.Union when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(unionHelpers[BuildUnionSignature(field)]).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Union:
                w.Raw("SizeOf").Identifier(unionHelpers[BuildUnionSignature(field)]).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Object when IsNullableValueField(field):
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(".Value)");
                break;
            case FieldKind.Object when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Object:
                w.Raw("SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Formatted when IsNullableValueField(field):
                w.Value(value).Raw(" is null ? SizeOfNil() : ").Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(".Value)");
                break;
            case FieldKind.Formatted when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : ").Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(")");
                break;
            case FieldKind.Formatted:
                w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(")");
                break;
            default:
                EmitScalarSizeExpression(w, field.Mapping, value);
                break;
        }
    }

    private static void EmitScalarSizeExpression(CodeWriter w, TypeMapping mapping, ValueExpr value)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
                w.Raw("SizeOfString(").Value(value).Raw(")");
                break;
            case FieldKind.ByteArray:
                w.Raw("SizeOfBytes(").Value(value).Raw(")");
                break;
            case FieldKind.Int32:
                w.Raw("SizeOfInt32(").Value(value).Raw(")");
                break;
            case FieldKind.Int64:
                w.Raw("SizeOfInt64(").Value(value).Raw(")");
                break;
            case FieldKind.Boolean:
                w.Raw("SizeOfBoolean(").Value(value).Raw(")");
                break;
            case FieldKind.Double:
                w.Raw("SizeOfDouble(").Value(value).Raw(")");
                break;
            case FieldKind.Decimal:
                w.Raw("SizeOfDecimal(").Value(value).Raw(")");
                break;
            case FieldKind.Guid:
                w.Raw("SizeOfGuid(").Value(value).Raw(")");
                break;
            case FieldKind.DateTime:
                w.Raw("SizeOfDateTime(").Value(value).Raw(")");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("SizeOfDateTimeOffset(").Value(value).Raw(")");
                break;
            case FieldKind.ActorRef:
                w.Raw("SizeOfActorRef(").Value(value).Raw(")");
                break;
            case FieldKind.Enum:
                w.Raw("SizeOfEnum((int)").Value(value).Raw(")");
                break;
            default:
                w.Raw("global::Akka.Serialization.SerializerV2.UnknownSize");
                break;
        }
    }

    private static void GenerateWriteMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, string> unionHelpers)
    {
        w.Raw("private void Write").Identifier(GetMessageMethodName(message))
            .Raw("(ref global::MessagePack.MessagePackWriter writer, ").Type(TypeName.Global(message.FullyQualifiedName)).Line(" message)");
        using (w.Block())
        {
            w.Raw("writer.WriteMapHeader(").Number(message.Fields.Length).Line(");");
            var alloc = new NameAlloc();
            foreach (var field in message.Fields)
                GenerateWriteField(w, unionHelpers, field, alloc);
        }

        w.BlankLine();
    }

    private static void GenerateReadMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, string> unionHelpers)
    {
        w.Raw("private ").Type(TypeName.Global(message.FullyQualifiedName)).Raw(" Read").Identifier(GetMessageMethodName(message))
            .Line("(ref global::MessagePack.MessagePackReader reader)");
        using (w.Block())
        {
            // Generator-owned locals are prefixed "__" so they cannot collide with a per-field local
            // (Local.ForField(field.Name)/GetHasLocal below), no matter what the [AkkaField] property
            // is named -- including adversarial names like "FieldCount" or "EntryIndex" that would
            // otherwise camel-case straight into these identifiers (CS0128/CS0136).
            w.Line("var __fieldCount = reader.ReadMapHeader();");
            var alloc = new NameAlloc();
            foreach (var field in message.Fields)
            {
                w.Type(TypeName.Global(field.TypeFullName));
                if (IsReferenceLike(field))
                    w.Raw("?");
                w.Raw(" ").Local(Local.ForField(field.Name)).Raw(" = ").Raw(DefaultValue(field)).Line(";");
                if (IsRequired(field))
                    w.Raw("var ").Local(GetHasLocal(field)).Line(" = false;");
            }

            w.Line("for (var __entryIndex = 0; __entryIndex < __fieldCount; __entryIndex++)");
            using (w.Block())
            {
                w.Line("var __fieldId = reader.ReadInt32();");
                w.Line("switch (__fieldId)");
                using (var sw = w.Switch())
                {
                    foreach (var field in message.Fields)
                    {
                        using (sw.CaseNumber(field.Index))
                        {
                            GenerateReadField(w, unionHelpers, field, alloc);
                            if (IsRequired(field))
                                w.Local(GetHasLocal(field)).Line(" = true;");
                            w.Line("break;");
                        }
                    }

                    using (sw.Default())
                    {
                        w.Line("reader.Skip();");
                        w.Line("break;");
                    }
                }
            }

            w.BlankLine();

            foreach (var field in message.Fields.Where(IsRequired))
            {
                var target = Local.ForField(field.Name);
                w.Raw("if (!").Local(GetHasLocal(field));
                if (IsReferenceLike(field))
                    w.Raw(" || ").Local(target).Raw(" is null");
                w.Line(")");
                using (w.Indented())
                {
                    w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Missing required field [")
                        .LiteralText(field.Name).Raw("] with index [").Number(field.Index).Raw("] while deserializing [")
                        .LiteralText(message.FullyQualifiedName).Line("].\");");
                }
            }

            GenerateReadMessageConstruction(w, message);
        }

        w.BlankLine();
    }

    /// <summary>
    /// Emits the final <c>return new T(...)</c> of a read method from <see cref="MessageInfo.ConstructionPlan"/>:
    /// NAMED arguments (escaped where the parameter name is a C# keyword, e.g. <c>@event:</c>, via
    /// the writer's <see cref="CodeWriter.Identifier"/> path) for every constructor-mapped
    /// [AkkaField] property, followed by an object initializer for whatever is left over. The plan
    /// stores field NAMES rather than <see cref="FieldInfo"/> references so it stays correct across
    /// <see cref="MessageInfo.WithFields"/> (formatter resolution can replace a field's mapping
    /// without touching its name).
    /// </summary>
    private static void GenerateReadMessageConstruction(CodeWriter w, MessageInfo message)
    {
        var fieldsByName = message.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var plan = message.ConstructionPlan;

        w.Raw("return new ").Type(TypeName.Global(message.FullyQualifiedName)).Raw("(");
        var firstArgument = true;
        foreach (var argument in plan.Arguments)
        {
            if (!firstArgument)
                w.Raw(", ");
            firstArgument = false;
            w.Identifier(argument.ParameterName).Raw(": ").Value(GetFieldValueExpression(fieldsByName[argument.FieldName]));
        }

        w.Raw(")");

        if (plan.InitializerFieldNames.Length > 0)
        {
            using (w.InlineBraces())
            {
                var firstInitializer = true;
                foreach (var name in plan.InitializerFieldNames)
                {
                    if (!firstInitializer)
                        w.Raw(", ");
                    firstInitializer = false;
                    var field = fieldsByName[name];
                    w.Identifier(field.Name).Raw(" = ").Value(GetFieldValueExpression(field));
                }
            }
        }

        w.Line(";");
    }

    // ---------------------------------------------------------------------------------------------
    // Union emission ([AkkaUnion] fields).
    //
    // A union value encodes as a 2-entry int-keyed map: { 1: <member manifest string>, 2: <the
    // member's ordinary inline field map> }. The manifest is the discriminator -- the same
    // serializer-owned manifest the member would carry as a top-level message -- so a value reads
    // identically whether it arrived through union dispatch or ordinary manifest dispatch. Contrast
    // with an object-typed envelope field's { 1: serializerId, 2: manifest, 3: opaque bytes }: the
    // union omits the serializer id (every member is owned by this serializer) and inlines the member's
    // fields directly instead of double-buffering them into a length-prefixed blob.
    //
    // Write dispatch matches the runtime type EXACTLY (value.GetType() == typeof(Member)) rather
    // than pattern matching, so an undeclared subtype of a declared member fails serialization
    // instead of silently truncating to its base -- the same default System.Text.Json applies to
    // [JsonDerivedType] sets (UnknownDerivedTypeHandling.FailSerialization). The size path returns
    // UnknownSize for an undeclared type instead of throwing; the write path throws.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The dedup identity of a union dispatch helper: the field's static type plus the ordered
    /// member set. Two fields (in the same or different messages) with the same static type and
    /// member set -- the common case under type-level [AkkaUnion] declarations -- share one
    /// generated Write/Read/SizeOf helper trio instead of emitting duplicates per field.
    /// </summary>
    private static string BuildUnionSignature(FieldInfo field)
    {
        return field.TypeFullName + "|" + string.Join("|", field.UnionMembers.Select(member => member.TypeFullName));
    }

    /// <summary>
    /// Plans one helper per distinct union signature across all reachable messages, as a
    /// <see cref="UnionPlan"/> whose <see cref="UnionPlan.Helpers"/> are ALREADY ordered by helper
    /// name (the order <see cref="GenerateUnionHelpers"/> used to derive itself, via
    /// <c>unionHelpers.Values.OrderBy(...)</c>, every time it ran) and whose member lists are
    /// ALREADY resolved to <see cref="ClosedSet"/>s against <paramref name="messagesByType"/> --
    /// baking both derivations in here, once, at resolve time keeps <see cref="ResolvedSerializer"/>
    /// a pure function of its inputs and keeps <see cref="Generate"/> from having to re-touch
    /// <paramref name="messagesByType"/> at all. Helpers are named after the union's folded static
    /// type ("Union_IOrderEvent"); when several distinct member sets share a static type
    /// (field-level overrides), later ones -- ordered by signature for determinism -- get a numeric
    /// suffix.
    /// </summary>
    private static UnionPlan PlanUnionHelpers(ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var representatives = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        foreach (var message in reachableMessages)
        {
            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                var signature = BuildUnionSignature(field);
                if (!representatives.ContainsKey(signature))
                    representatives[signature] = field;
            }
        }

        var helpers = ImmutableArray.CreateBuilder<UnionHelperPlan>();
        foreach (var group in representatives.GroupBy(pair => FoldTypeName(pair.Value.TypeFullName), StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var helperName = i == 0 ? "Union_" + group.Key : "Union_" + group.Key + "_" + (i + 1);
                var field = ordered[i].Value;
                helpers.Add(new UnionHelperPlan(ordered[i].Key, helperName, field.TypeFullName, BuildUnionMembers(field, messagesByType)));
            }
        }

        // GenerateUnionHelpers used to derive this ordering itself, on every (re)generation, from
        // the dictionary's Values; baking it in here means Generate() can iterate Helpers as-is.
        var orderedHelpers = helpers.ToImmutable().Sort((a, b) => string.CompareOrdinal(a.HelperName, b.HelperName));
        return new UnionPlan(orderedHelpers);
    }

    /// <summary>
    /// The declared members of one union field, filtered to the supported/known ones (mirrors the
    /// old inline filter in <see cref="GenerateUnionHelpers"/>: <c>member.IsSupported &amp;&amp;
    /// messagesByType.ContainsKey(...)</c>) and reduced to the (type name, manifest, method name)
    /// triple every union write/read/size helper actually needs -- exactly a <see cref="ClosedSet"/>.
    /// </summary>
    private static ClosedSet BuildUnionMembers(FieldInfo field, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var builder = ImmutableArray.CreateBuilder<ClosedSetMember>();
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.TypeFullName, out var memberMessage))
                continue;

            builder.Add(new ClosedSetMember(member.TypeFullName, memberMessage.Manifest, GetMessageMethodName(memberMessage)));
        }

        return new ClosedSet(builder.ToImmutable());
    }

    private static void GenerateUnionHelpers(CodeWriter w, UnionPlan unionPlan)
    {
        // Already ordered by helper name -- see PlanUnionHelpers.
        foreach (var helper in unionPlan.Helpers)
        {
            GenerateUnionWrite(w, helper);
            GenerateUnionRead(w, helper);
            GenerateUnionSize(w, helper);
        }
    }

    private static void GenerateUnionWrite(CodeWriter w, UnionHelperPlan helper)
    {
        w.Raw("private void Write").Identifier(helper.HelperName)
            .Raw("(ref global::MessagePack.MessagePackWriter writer, ").Type(TypeName.Global(helper.FieldTypeFullName)).Line(" value)");
        using (w.Block())
        {
            w.Line("var runtimeType = value.GetType();");
            foreach (var member in helper.Members.Members)
            {
                w.Raw("if (runtimeType == typeof(").Type(TypeName.Global(member.TypeFullName)).Line("))");
                using (w.Block())
                {
                    w.Line("writer.WriteMapHeader(2);");
                    w.Line("writer.Write(1);");
                    w.Raw("writer.Write(").StringLiteral(member.Manifest).Line(");");
                    w.Line("writer.Write(2);");
                    w.Raw("Write").Identifier(member.MethodName).Raw("(ref writer, (").Type(TypeName.Global(member.TypeFullName)).Line(")value);");
                    w.Line("return;");
                }

                w.BlankLine();
            }

            w.Raw("throw new global::System.Runtime.Serialization.SerializationException($\"Type [{runtimeType}] is not a declared union member for union [")
                .LiteralText(helper.FieldTypeFullName).Line("].\");");
        }

        w.BlankLine();
    }

    private static void GenerateUnionRead(CodeWriter w, UnionHelperPlan helper)
    {
        w.Raw("private ").Type(TypeName.Global(helper.FieldTypeFullName)).Raw(" Read").Identifier(helper.HelperName)
            .Line("(ref global::MessagePack.MessagePackReader reader)");
        using (w.Block())
        {
            w.Line("var fieldCount = reader.ReadMapHeader();");
            w.Line("string? manifest = null;");
            w.Type(TypeName.Global(helper.FieldTypeFullName)).Line("? result = default;");
            w.Line("var hasPayload = false;");
            w.Line("for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)");
            using (w.Block())
            {
                w.Line("var fieldId = reader.ReadInt32();");
                w.Line("switch (fieldId)");
                using (var sw = w.Switch())
                {
                    using (sw.CaseNumber(1))
                    {
                        w.Line("manifest = reader.ReadString();");
                        w.Line("break;");
                    }

                    using (sw.CaseNumber(2))
                    {
                        w.Line("switch (manifest)");
                        using (var manifestSwitch = w.Switch())
                        {
                            foreach (var member in helper.Members.Members)
                            {
                                using (manifestSwitch.CaseStringLiteral(member.Manifest))
                                {
                                    w.Raw("result = Read").Identifier(member.MethodName).Line("(ref reader);");
                                    w.Line("break;");
                                }
                            }

                            using (manifestSwitch.CaseNull())
                            {
                                w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Union manifest must precede the payload for union [")
                                    .LiteralText(helper.FieldTypeFullName).Line("].\");");
                            }

                            using (manifestSwitch.Default())
                            {
                                w.Raw("throw new global::System.Runtime.Serialization.SerializationException($\"Unknown union manifest [{manifest}] for union [")
                                    .LiteralText(helper.FieldTypeFullName).Line("].\");");
                            }
                        }

                        w.BlankLine();
                        w.Line("hasPayload = true;");
                        w.Line("break;");
                    }

                    using (sw.Default())
                    {
                        w.Line("reader.Skip();");
                        w.Line("break;");
                    }
                }
            }

            w.BlankLine();
            w.Line("if (!hasPayload || result is null)");
            using (w.Indented())
            {
                w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Missing union payload for union [")
                    .LiteralText(helper.FieldTypeFullName).Line("].\");");
            }

            w.Line("return result;");
        }

        w.BlankLine();
    }

    private static void GenerateUnionSize(CodeWriter w, UnionHelperPlan helper)
    {
        w.Raw("private int SizeOf").Identifier(helper.HelperName)
            .Raw("(").Type(TypeName.Global(helper.FieldTypeFullName)).Line(" value)");
        using (w.Block())
        {
            w.Line("var runtimeType = value.GetType();");
            foreach (var member in helper.Members.Members)
            {
                w.Raw("if (runtimeType == typeof(").Type(TypeName.Global(member.TypeFullName)).Line("))");
                using (w.Block())
                {
                    w.Raw("var payloadSize = SizeOf").Identifier(member.MethodName).Raw("((").Type(TypeName.Global(member.TypeFullName)).Line(")value);");
                    w.Line("if (payloadSize < 0)");
                    using (w.Indented())
                        w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
                    w.Raw("return checked(SizeOfMapHeader(2) + SizeOfInt32(1) + SizeOfString(").StringLiteral(member.Manifest)
                        .Line(") + SizeOfInt32(2) + payloadSize);");
                }

                w.BlankLine();
            }

            w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
        }

        w.BlankLine();
    }

    private static void GenerateWriteField(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = ValueExpr.GeneratorOwned("message").Member(field.Name);
        w.Raw("writer.Write(").Number(field.Index).Line(");");
        if (IsNullableValueField(field))
        {
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                GenerateWriteFieldValue(w, unionHelpers, field, value.Member("Value"), alloc);
            return;
        }

        GenerateWriteFieldValue(w, unionHelpers, field, value, alloc);
    }

    private static void GenerateWriteFieldValue(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, ValueExpr value, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitWriteCollectionBody(w, field.Mapping, value, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
            case FieldKind.ByteArray:
            case FieldKind.Int32:
            case FieldKind.Int64:
            case FieldKind.Boolean:
            case FieldKind.Double:
                w.Raw("writer.Write(").Value(value).Line(");");
                break;
            case FieldKind.Decimal:
                w.Raw("WriteDecimal(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Guid:
                w.Raw("WriteGuid(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTime:
                w.Raw("WriteDateTime(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("WriteDateTimeOffset(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.ActorRef:
                w.Raw("WriteActorRef(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.EnvelopePayload:
                w.Raw("WriteEnvelopePayload(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Enum:
                w.Raw("writer.Write((int)").Value(value).Line(");");
                break;
            case FieldKind.Object:
                // Mirrors FieldKind.Formatted below: when the nested type is a value type, a
                // nullable field was already unwrapped to its non-nullable .Value by the caller
                // (GenerateWriteField's IsNullableValueField branch), so no further null-check is
                // possible (or needed) here -- only a genuinely nullable REFERENCE nested type
                // needs the runtime "is null" guard.
                if (field.IsNullable && IsReferenceLike(field))
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Raw("Write").Identifier(GetObjectMethodName(field.Mapping)).Raw("(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Raw("Write").Identifier(GetObjectMethodName(field.Mapping)).Raw("(ref writer, ").Value(value).Line(");");
                }
                break;
            case FieldKind.Formatted:
                if (field.IsNullable && IsReferenceLike(field))
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".Write(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".Write(ref writer, ").Value(value).Line(");");
                }
                break;
            case FieldKind.Union:
                // Union fields are always reference-like (the static type is an interface or
                // abstract base), so only the nullable-reference guard is needed here.
                if (field.IsNullable)
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Raw("Write").Identifier(unionHelpers[BuildUnionSignature(field)]).Raw("(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Raw("Write").Identifier(unionHelpers[BuildUnionSignature(field)]).Raw("(ref writer, ").Value(value).Line(");");
                }
                break;
        }
    }

    private static void GenerateReadField(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var target = Local.ForField(field.Name);

        // Collection fields own their MessagePack nil handling end-to-end (EmitReadCollectionBody
        // does its own TryReadNil), so they are read directly regardless of the field's nullability:
        // a nil-on-the-wire assigns null, and the post-loop required-field guard rejects a null in a
        // non-nullable collection slot exactly as it does for any other non-nullable reference field.
        if (IsCollectionKind(field.Mapping.Kind))
        {
            GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        if (IsNullableValueField(field))
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(target).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        var isNullableReferenceLikeSlot = field.Mapping.Kind == FieldKind.EnvelopePayload
            || field.Mapping.Kind == FieldKind.Union
            || (field.Mapping.Kind == FieldKind.Object && IsReferenceLike(field))
            || (field.Mapping.Kind == FieldKind.Formatted && IsReferenceLike(field));

        if (isNullableReferenceLikeSlot && field.IsNullable)
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(target).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
    }

    private static void GenerateReadFieldValue(CodeWriter w, ImmutableDictionary<string, string> unionHelpers, FieldInfo field, Local target, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitReadCollectionBody(w, field.Mapping, target, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                w.Local(target).Line(" = reader.ReadString();");
                break;
            case FieldKind.ByteArray:
                w.Raw("var ").Local(target.WithSuffix("Bytes")).Line(" = reader.ReadBytes();");
                w.Local(target).Raw(" = ").Local(target.WithSuffix("Bytes")).Line("?.ToArray();");
                break;
            case FieldKind.Int32:
                w.Local(target).Line(" = reader.ReadInt32();");
                break;
            case FieldKind.Int64:
                w.Local(target).Line(" = reader.ReadInt64();");
                break;
            case FieldKind.Boolean:
                w.Local(target).Line(" = reader.ReadBoolean();");
                break;
            case FieldKind.Double:
                w.Local(target).Line(" = reader.ReadDouble();");
                break;
            case FieldKind.Decimal:
                w.Local(target).Line(" = ReadDecimal(ref reader);");
                break;
            case FieldKind.Guid:
                w.Local(target).Line(" = ReadGuid(ref reader);");
                break;
            case FieldKind.DateTime:
                w.Local(target).Line(" = ReadDateTime(ref reader);");
                break;
            case FieldKind.DateTimeOffset:
                w.Local(target).Line(" = ReadDateTimeOffset(ref reader);");
                break;
            case FieldKind.ActorRef:
                w.Local(target).Line(" = ReadActorRef(ref reader);");
                break;
            case FieldKind.EnvelopePayload:
                w.Local(target).Raw(" = ReadEnvelopePayload<").Type(TypeName.Global(field.TypeFullName)).Line(">(ref reader);");
                break;
            case FieldKind.Enum:
                w.Local(target).Raw(" = (").Type(TypeName.Global(field.Mapping.TypeFullName)).Line(")reader.ReadInt32();");
                break;
            case FieldKind.Object:
                w.Local(target).Raw(" = Read").Identifier(GetObjectMethodName(field.Mapping)).Line("(ref reader);");
                break;
            case FieldKind.Formatted:
                w.Local(target).Raw(" = ").Identifier(GetFormatterFieldName(field.Formatter!)).Line(".Read(ref reader);");
                break;
            case FieldKind.Union:
                w.Local(target).Raw(" = Read").Identifier(unionHelpers[BuildUnionSignature(field)]).Line("(ref reader);");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Native collection emission: T[], List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>,
    // Dictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue>, ImmutableArray<T>, ImmutableList<T>,
    // ImmutableHashSet<T>, ImmutableDictionary<TKey,TValue>.
    //
    // Collections encode as MessagePack array/map framing wrapped around per-element encodings that
    // reuse the same scalar/object primitives as ordinary fields, and compose recursively so nested
    // collections (List<List<int>>, Dictionary<string, List<Reading>>, ImmutableDictionary<string,
    // List<int>>) work with no special cases. Every collection kind shares this SAME wire framing --
    // an ImmutableList<int> field is byte-identical on the wire to the same data in a List<int> field
    // (see CollectionFieldSpec.cs); only the in-memory construction on read differs per kind:
    //   - T[]                                -> T[] (indexer-set, pre-sized)
    //   - List<T>, IReadOnlyList<T>,
    //     IReadOnlyCollection<T>             -> List<T> (Add, pre-sized capacity)
    //   - Dictionary<K,V>,
    //     IReadOnlyDictionary<K,V>           -> Dictionary<K,V> (indexer-set, pre-sized capacity)
    //   - ImmutableArray<T>                  -> ImmutableArray.CreateBuilder<T>(capacity), then
    //                                            Builder.MoveToImmutable() (zero-copy handoff; the
    //                                            builder is pre-sized to the wire's element count, so
    //                                            Count always equals Capacity when the loop finishes)
    //   - ImmutableList<T>, ImmutableHashSet<T>,
    //     ImmutableDictionary<K,V>           -> the type's own Builder (no capacity parameter -- these
    //                                            are tree/trie-backed, not array-backed), then
    //                                            Builder.ToImmutable()
    // A duplicate key written into a Dictionary/IReadOnlyDictionary/ImmutableDictionary on read is
    // last-write-wins (indexer-set), matching Dictionary<K,V>'s own semantics. A duplicate element
    // written into an ImmutableHashSet on read is silently deduplicated (Builder.Add returns false
    // for an already-present value; the return is ignored), matching a normal set's Add semantics.
    // Set/map ITERATION ORDER on write is whatever GetEnumerator() yields for that runtime type --
    // NOT guaranteed stable for ImmutableHashSet<T>/ImmutableDictionary<K,V> across instances built
    // from the same logical content in a different order, so tests must not byte-compare a written
    // multi-element set/dictionary; only its round-tripped VALUES are guaranteed (see
    // CollectionFieldSpec.cs's ImmutableHashSet/ImmutableDictionary tests, which sort before
    // comparing).
    //
    // ImmutableArray<T> is the one VALUE-typed (struct) collection kind (IsStructCollectionKind), so
    // it cannot use "value is null" (CS0037: not convertible from a non-nullable value type) -- its
    // null-ish state is default(ImmutableArray<T>).IsDefault, which is DISTINCT from
    // ImmutableArray<T>.Empty (Length 0, IsDefault false). This generator maps that distinction onto
    // the SAME nil-vs-empty wire framing every other collection kind already uses:
    //   - write: value.IsDefault  -> MessagePack nil   (mirrors "value is null" for every other kind)
    //   - write: value.Empty (or any non-default, zero-length array) -> array header 0
    //   - read:  nil  -> default(ImmutableArray<T>)    (mirrors "target = null" for every other kind)
    //   - read:  array header 0 (non-nil) -> a Builder(0).MoveToImmutable(), which is
    //            ImmutableArray<T>.Empty (Length 0, IsDefault FALSE) -- distinct from the nil case
    // This makes default(ImmutableArray<T>) round-trip losslessly as itself, exactly as null already
    // round-trips for every reference collection kind, while a genuinely empty array stays
    // distinguishable from both on the wire and after deserialization. Accessing .Length or
    // enumerating a default ImmutableArray<T> throws NullReferenceException at runtime (verified
    // against the in-box System.Collections.Immutable on net10.0), so EVERY code path touching an
    // ImmutableArray<T> value (write, size) MUST check .IsDefault first -- never assume "not null"
    // is enough, the way it is for a reference collection.
    // null encodes as MessagePack nil; empty encodes as a zero-length array/map header. The two are
    // distinct on the wire and round-trip as distinct values. This framing is permanent wire format --
    // see the encoding matrix in the PR body for the full table.
    // ---------------------------------------------------------------------------------------------

    // ImmutableArray<T> has no public Count (it is an explicit ICollection<T>/IReadOnlyCollection<T>
    // implementation, inaccessible on the struct type directly) -- only Length, exactly like T[].
    private static string CollectionCountMember(FieldKind kind) => kind is FieldKind.Array or FieldKind.ImmutableArray ? "Length" : "Count";

    /// <summary>
    /// Whether a value of this element mapping is stored as a reference in its strongly-typed collection
    /// slot. Reference elements are declared as nullable read temporaries and stored with the
    /// null-forgiving operator (a runtime no-op) so the generated code stays warning-clean under
    /// <c>#nullable enable</c> while still round-tripping a genuine null element.
    /// <see cref="FieldKind.ImmutableArray"/> is deliberately excluded even though it is a collection
    /// kind: it is a VALUE type element (see <see cref="IsStructCollectionKind"/>), so wrapping its read
    /// temporary in <c>Nullable&lt;ImmutableArray&lt;T&gt;&gt;</c> and null-forgiving it back would not
    /// even compile against a <c>List&lt;ImmutableArray&lt;T&gt;&gt;.Add(ImmutableArray&lt;T&gt;)</c> slot.
    /// </summary>
    private static bool ElementIsReference(TypeMapping mapping)
    {
        if (IsStructCollectionKind(mapping.Kind))
            return false;

        if (IsCollectionKind(mapping.Kind))
            return true;

        return mapping.Kind switch
        {
            FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef => true,
            FieldKind.Object => !mapping.IsValueType,
            _ => false
        };
    }

    private static ValueExpr ElementStore(TypeMapping mapping, ValueExpr valueExpr)
        => ElementIsReference(mapping) ? valueExpr.NullForgiven() : valueExpr;

    private static bool IsScalarValueKind(FieldKind kind)
        => kind is FieldKind.Int32 or FieldKind.Int64 or FieldKind.Boolean or FieldKind.Double
            or FieldKind.Decimal or FieldKind.Guid or FieldKind.DateTime or FieldKind.DateTimeOffset or FieldKind.Enum;

    // ----- WRITE -----

    private static void EmitWriteCollectionBody(CodeWriter w, TypeMapping mapping, ValueExpr value, NameAlloc alloc)
    {
        w.Raw("if (").Value(value).Line(IsStructCollectionKind(mapping.Kind) ? ".IsDefault)" : " is null)");
        using (w.Block())
            w.Line("writer.WriteNil();");
        w.Line("else");
        using (w.Block())
        {
            if (IsMapLikeKind(mapping.Kind))
            {
                var kvp = alloc.Next("kvp");
                w.Raw("writer.WriteMapHeader(").Value(value).Line(".Count);");
                w.Raw("foreach (var ").Local(kvp).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                {
                    EmitWriteElement(w, mapping.TypeArguments[0], ((ValueExpr)kvp).Member("Key"), alloc);
                    EmitWriteElement(w, mapping.TypeArguments[1], ((ValueExpr)kvp).Member("Value"), alloc);
                }
            }
            else
            {
                var item = alloc.Next("item");
                w.Raw("writer.WriteArrayHeader(").Value(value.Member(CollectionCountMember(mapping.Kind))).Line(");");
                w.Raw("foreach (var ").Local(item).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                    EmitWriteElement(w, mapping.TypeArguments[0], item, alloc);
            }
        }
    }

    private static void EmitWriteElement(CodeWriter w, TypeMapping mapping, ValueExpr value, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            EmitWriteCollectionBody(w, mapping, value, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Raw("Write").Identifier(GetObjectMethodName(mapping)).Raw("(ref writer, ").Value(value).Line(");");
                return;
            }

            var writeValue = mapping.IsValueType ? value.Member("Value") : value;
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                w.Raw("Write").Identifier(GetObjectMethodName(mapping)).Raw("(ref writer, ").Value(writeValue).Line(");");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                EmitScalarWrite(w, mapping, value.Member("Value"));
            return;
        }

        EmitScalarWrite(w, mapping, value);
    }

    private static void EmitScalarWrite(CodeWriter w, TypeMapping mapping, ValueExpr value)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
            case FieldKind.ByteArray:
            case FieldKind.Int32:
            case FieldKind.Int64:
            case FieldKind.Boolean:
            case FieldKind.Double:
                w.Raw("writer.Write(").Value(value).Line(");");
                break;
            case FieldKind.Decimal:
                w.Raw("WriteDecimal(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Guid:
                w.Raw("WriteGuid(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTime:
                w.Raw("WriteDateTime(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("WriteDateTimeOffset(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.ActorRef:
                w.Raw("WriteActorRef(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Enum:
                w.Raw("writer.Write((int)").Value(value).Line(");");
                break;
        }
    }

    // ----- READ -----

    private static void EmitReadCollectionBody(CodeWriter w, TypeMapping mapping, Local target, NameAlloc alloc)
    {
        w.Line("if (reader.TryReadNil())");
        using (w.Block())
            w.Local(target).Raw(" = ").Raw(NilCollectionValue(mapping.Kind)).Line(";");
        w.Line("else");
        using (w.Block())
        {
            var length = alloc.Next("len");
            var collection = alloc.Next("col");
            var index = alloc.Next("i");

            if (IsMapLikeKind(mapping.Kind))
            {
                var key = mapping.TypeArguments[0];
                var val = mapping.TypeArguments[1];
                var keyVar = alloc.Next("key");
                var valVar = alloc.Next("val");
                w.Raw("var ").Local(length).Line(" = reader.ReadMapHeader();");
                EmitMapLikeAllocation(w, mapping.Kind, key.DeclaredTypeName, val.DeclaredTypeName, collection, length);
                w.Raw("for (var ").Local(index).Raw(" = 0; ").Local(index).Raw(" < ").Local(length).Raw("; ").Local(index).Line("++)");
                using (w.Block())
                {
                    EmitReadElement(w, key, keyVar, alloc);
                    EmitReadElement(w, val, valVar, alloc);
                    // Last-write-wins on a duplicate key, matching Dictionary<K,V>'s own indexer semantics
                    // -- true for the plain Dictionary allocation above AND for ImmutableDictionary.Builder's
                    // indexer (verified: Builder[key] = value overwrites an existing entry, same as Dictionary).
                    w.Local(collection).Raw("[").Value(ElementStore(key, keyVar)).Raw("] = ").Value(ElementStore(val, valVar)).Line(";");
                }
            }
            else
            {
                var element = mapping.TypeArguments[0];
                var itemVar = alloc.Next("item");
                w.Raw("var ").Local(length).Line(" = reader.ReadArrayHeader();");
                EmitListLikeAllocation(w, mapping.Kind, element.DeclaredTypeName, collection, length);
                w.Raw("for (var ").Local(index).Raw(" = 0; ").Local(index).Raw(" < ").Local(length).Raw("; ").Local(index).Line("++)");
                using (w.Block())
                {
                    EmitReadElement(w, element, itemVar, alloc);
                    if (mapping.Kind == FieldKind.Array)
                        w.Local(collection).Raw("[").Local(index).Raw("] = ").Value(ElementStore(element, itemVar)).Line(";");
                    else
                        // Add() on every non-array kind: List<T>'s own Add, ImmutableArray<T>.Builder.Add
                        // (array-backed, pre-sized -- see EmitListLikeAllocation), ImmutableList<T>.Builder.Add,
                        // or ImmutableHashSet<T>.Builder.Add (silently ignores an already-present duplicate,
                        // matching a normal set's Add semantics -- its bool return is intentionally discarded).
                        w.Local(collection).Raw(".Add(").Value(ElementStore(element, itemVar)).Line(");");
                }
            }

            w.Local(target).Raw(" = ");
            EmitFinalizeCollectionExpression(w, mapping.Kind, collection);
            w.Line(";");
        }
    }

    /// <summary>
    /// The value assigned to the read target when the wire holds MessagePack nil. Every reference
    /// collection kind mirrors ordinary reference-field nil handling ("target = null"). The one
    /// VALUE-typed kind, <see cref="FieldKind.ImmutableArray"/>, cannot be assigned "null" (its local
    /// is declared as the plain non-nullable struct type -- see <see cref="IsReferenceLike"/> /
    /// <see cref="ElementIsReference"/>), so nil instead decodes to "default", i.e.
    /// <c>default(ImmutableArray&lt;T&gt;)</c>, whose <c>IsDefault</c> is true -- the read-side mirror
    /// of the write-side <c>value.IsDefault</c> check in <see cref="EmitWriteCollectionBody"/>.
    /// </summary>
    private static string NilCollectionValue(FieldKind kind) => IsStructCollectionKind(kind) ? "default" : "null";

    /// <summary>
    /// Emits the "var col = ...;" allocation that a list-like collection read builds into before the
    /// element loop. <see cref="FieldKind.Array"/> allocates the exact target array (jagged-aware, via
    /// <see cref="EmitArrayAllocationExpression"/>); <see cref="FieldKind.ImmutableArray"/> allocates its
    /// array-backed <c>Builder</c> pre-sized to <paramref name="lengthVar"/> so the loop's <c>Add</c>
    /// calls never resize and the final <c>MoveToImmutable()</c> (see
    /// <see cref="EmitFinalizeCollectionExpression"/>) is a zero-copy handoff instead of a defensive copy;
    /// every other list-like kind (<see cref="FieldKind.List"/>, <see cref="FieldKind.ReadOnlyList"/>,
    /// <see cref="FieldKind.ReadOnlyCollection"/>) materializes a pre-sized <c>List&lt;T&gt;</c>, and
    /// <see cref="FieldKind.ImmutableList"/>/<see cref="FieldKind.ImmutableHashSet"/> allocate their own
    /// tree/trie-backed <c>Builder</c> (no capacity parameter exists for either -- there is nothing
    /// array-like to pre-size).
    /// </summary>
    private static void EmitListLikeAllocation(CodeWriter w, FieldKind kind, string elementTypeName, Local collectionVar, Local lengthVar)
    {
        switch (kind)
        {
            case FieldKind.Array:
                w.Raw("var ").Local(collectionVar).Raw(" = ");
                EmitArrayAllocationExpression(w, elementTypeName, lengthVar);
                w.Line(";");
                break;
            case FieldKind.ImmutableArray:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableArray.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Raw(">(").Local(lengthVar).Line(");");
                break;
            case FieldKind.ImmutableList:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableList.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Line(">();");
                break;
            case FieldKind.ImmutableHashSet:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableHashSet.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Line(">();");
                break;
            default:
                w.Raw("var ").Local(collectionVar).Raw(" = new global::System.Collections.Generic.List<").Type(TypeName.Global(elementTypeName)).Raw(">(").Local(lengthVar).Line(");");
                break;
        }
    }

    /// <summary>
    /// Emits the "var col = ...;" allocation that a map-like collection read builds into before the
    /// entry loop. <see cref="FieldKind.ImmutableDictionary"/> allocates its own trie-backed
    /// <c>Builder</c> (no capacity parameter -- there is nothing array-like to pre-size); every other
    /// map-like kind (<see cref="FieldKind.Dictionary"/>, <see cref="FieldKind.ReadOnlyDictionary"/>)
    /// materializes a pre-sized <c>Dictionary&lt;TKey,TValue&gt;</c>.
    /// </summary>
    private static void EmitMapLikeAllocation(CodeWriter w, FieldKind kind, string keyTypeName, string valueTypeName, Local collectionVar, Local lengthVar)
    {
        if (kind == FieldKind.ImmutableDictionary)
        {
            w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<")
                .Type(TypeName.Global(keyTypeName)).Raw(", ").Type(TypeName.Global(valueTypeName)).Line(">();");
            return;
        }

        w.Raw("var ").Local(collectionVar).Raw(" = new global::System.Collections.Generic.Dictionary<")
            .Type(TypeName.Global(keyTypeName)).Raw(", ").Type(TypeName.Global(valueTypeName)).Raw(">(").Local(lengthVar).Line(");");
    }

    /// <summary>
    /// Emits the expression assigned to the read target once the element/entry loop finishes. Every
    /// kind that allocated its FINAL storage directly (<see cref="FieldKind.Array"/>, <see cref="FieldKind.List"/>
    /// and the read-only interfaces backed by it, <see cref="FieldKind.Dictionary"/> and
    /// <see cref="FieldKind.ReadOnlyDictionary"/>) assigns the collection variable as-is. Every kind
    /// that allocated a <c>Builder</c> instead (see <see cref="EmitListLikeAllocation"/> /
    /// <see cref="EmitMapLikeAllocation"/>) finalizes it here: <see cref="FieldKind.ImmutableArray"/>'s
    /// array-backed builder via <c>MoveToImmutable()</c> (zero-copy -- valid because the builder was
    /// pre-sized to exactly the element count the loop adds), every other <c>Immutable*</c> kind via
    /// <c>ToImmutable()</c>.
    /// </summary>
    private static void EmitFinalizeCollectionExpression(CodeWriter w, FieldKind kind, Local collectionVar)
    {
        switch (kind)
        {
            case FieldKind.ImmutableArray:
                w.Local(collectionVar).Raw(".MoveToImmutable()");
                break;
            case FieldKind.ImmutableList:
            case FieldKind.ImmutableHashSet:
            case FieldKind.ImmutableDictionary:
                w.Local(collectionVar).Raw(".ToImmutable()");
                break;
            default:
                w.Local(collectionVar);
                break;
        }
    }

    /// <summary>
    /// Emits the C# allocation expression for a single-dimension array of
    /// <paramref name="elementTypeName"/>. For a jagged array the length belongs in the FIRST bracket
    /// pair with the element's own bracket pairs appended after it: element <c>int[]</c> allocates as
    /// <c>new int[len][]</c> (not the invalid <c>new int[][len]</c>), element <c>int[][]</c> as
    /// <c>new int[len][][]</c>. Bracket pairs only ever appear as an array suffix in the
    /// fully-qualified display name (generics use angle brackets), so peeling trailing <c>[]</c> pairs
    /// off the element type name recovers the correct structure.
    /// </summary>
    private static void EmitArrayAllocationExpression(CodeWriter w, string elementTypeName, Local lengthVar)
    {
        var core = elementTypeName;
        var suffix = string.Empty;
        while (core.EndsWith("[]", StringComparison.Ordinal))
        {
            core = core.Substring(0, core.Length - 2);
            suffix += "[]";
        }

        w.Raw("new ").Type(TypeName.Global(core)).Raw("[").Local(lengthVar).Raw("]").Raw(suffix);
    }

    private static void EmitReadElement(CodeWriter w, TypeMapping mapping, Local resultVar, NameAlloc alloc)
    {
        // The read temporary's declared type: reference elements get the nullable form and are
        // stored with the null-forgiving operator (see ElementIsReference/ElementStore).
        w.Type(TypeName.Global(mapping.DeclaredTypeName));
        if (ElementIsReference(mapping))
            w.Raw("?");
        w.Raw(" ").Local(resultVar).Line(";");

        if (IsCollectionKind(mapping.Kind))
        {
            EmitReadCollectionBody(w, mapping, resultVar, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Local(resultVar).Raw(" = Read").Identifier(GetObjectMethodName(mapping)).Line("(ref reader);");
                return;
            }

            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(resultVar).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                w.Local(resultVar).Raw(" = Read").Identifier(GetObjectMethodName(mapping)).Line("(ref reader);");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(resultVar).Line(" = null;");
            w.Line("else");
            using (w.Indented())
            {
                w.Local(resultVar).Raw(" = ");
                EmitScalarReadExpression(w, mapping);
                w.Line(";");
            }

            return;
        }

        w.Local(resultVar).Raw(" = ");
        EmitScalarReadExpression(w, mapping);
        w.Line(";");
    }

    private static void EmitScalarReadExpression(CodeWriter w, TypeMapping mapping)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
                w.Raw("reader.ReadString()");
                break;
            case FieldKind.ByteArray:
                w.Raw("reader.ReadBytes()?.ToArray()");
                break;
            case FieldKind.Int32:
                w.Raw("reader.ReadInt32()");
                break;
            case FieldKind.Int64:
                w.Raw("reader.ReadInt64()");
                break;
            case FieldKind.Boolean:
                w.Raw("reader.ReadBoolean()");
                break;
            case FieldKind.Double:
                w.Raw("reader.ReadDouble()");
                break;
            case FieldKind.Decimal:
                w.Raw("ReadDecimal(ref reader)");
                break;
            case FieldKind.Guid:
                w.Raw("ReadGuid(ref reader)");
                break;
            case FieldKind.DateTime:
                w.Raw("ReadDateTime(ref reader)");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("ReadDateTimeOffset(ref reader)");
                break;
            case FieldKind.ActorRef:
                w.Raw("ReadActorRef(ref reader)");
                break;
            case FieldKind.Enum:
                w.Raw("(").Type(TypeName.Global(mapping.TypeFullName)).Raw(")reader.ReadInt32()");
                break;
            default:
                w.Raw("default");
                break;
        }
    }

    // ----- SIZE -----

    private static void EmitSizeCollectionBody(CodeWriter w, TypeMapping mapping, ValueExpr value, Local sizeVar, NameAlloc alloc)
    {
        w.Raw("int ").Local(sizeVar).Line(";");
        w.Raw("if (").Value(value).Line(IsStructCollectionKind(mapping.Kind) ? ".IsDefault)" : " is null)");
        using (w.Block())
            w.Local(sizeVar).Line(" = SizeOfNil();");
        w.Line("else");
        using (w.Block())
        {
            if (IsMapLikeKind(mapping.Kind))
            {
                var kvp = alloc.Next("kvp");
                w.Local(sizeVar).Raw(" = SizeOfMapHeader(").Value(value).Line(".Count);");
                w.Raw("foreach (var ").Local(kvp).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                {
                    EmitSizeElement(w, mapping.TypeArguments[0], ((ValueExpr)kvp).Member("Key"), sizeVar, alloc);
                    EmitSizeElement(w, mapping.TypeArguments[1], ((ValueExpr)kvp).Member("Value"), sizeVar, alloc);
                }
            }
            else
            {
                var item = alloc.Next("item");
                w.Local(sizeVar).Raw(" = SizeOfArrayHeader(").Value(value.Member(CollectionCountMember(mapping.Kind))).Line(");");
                w.Raw("foreach (var ").Local(item).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                    EmitSizeElement(w, mapping.TypeArguments[0], item, sizeVar, alloc);
            }
        }
    }

    private static void EmitSizeElement(CodeWriter w, TypeMapping mapping, ValueExpr value, Local sizeVar, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            var innerSize = alloc.Next("size");
            EmitSizeCollectionBody(w, mapping, value, innerSize, alloc);
            w.Local(sizeVar).Raw(" += ").Local(innerSize).Line(";");
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            var elementSize = alloc.Next("size");
            w.Raw("var ").Local(elementSize).Raw(" = ");
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Raw("SizeOf").Identifier(GetObjectMethodName(mapping)).Raw("(").Value(value).Raw(")");
            }
            else
            {
                var sizedValue = mapping.IsValueType ? value.Member("Value") : value;
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(mapping)).Raw("(").Value(sizedValue).Raw(")");
            }

            w.Line(";");
            w.Raw("if (").Local(elementSize).Line(" < 0)");
            using (w.Indented())
                w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
            w.Local(sizeVar).Raw(" += ").Local(elementSize).Line(";");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Local(sizeVar).Raw(" += ").Value(value).Raw(" is null ? SizeOfNil() : ");
            EmitScalarSizeExpression(w, mapping, value.Member("Value"));
            w.Line(";");
            return;
        }

        w.Local(sizeVar).Raw(" += ");
        EmitScalarSizeExpression(w, mapping, value);
        w.Line(";");
    }

    private static bool IsCollectionKind(FieldKind kind)
        => kind is FieldKind.Array or FieldKind.List or FieldKind.ReadOnlyList or FieldKind.Dictionary
            or FieldKind.ReadOnlyCollection or FieldKind.ReadOnlyDictionary
            or FieldKind.ImmutableArray or FieldKind.ImmutableList or FieldKind.ImmutableHashSet or FieldKind.ImmutableDictionary;

    /// <summary>Whether a collection kind encodes as a MessagePack MAP (key/value pairs) rather than an ARRAY.</summary>
    private static bool IsMapLikeKind(FieldKind kind)
        => kind is FieldKind.Dictionary or FieldKind.ReadOnlyDictionary or FieldKind.ImmutableDictionary;

    /// <summary>
    /// Whether a collection kind is a VALUE type (struct) rather than a reference type. Only
    /// <see cref="FieldKind.ImmutableArray"/> qualifies today: every other collection kind (arrays,
    /// <c>List&lt;T&gt;</c>, the read-only interfaces, <c>ImmutableList/HashSet/Dictionary</c>) is a
    /// reference type, so a null-ish value is a genuine CLR <c>null</c> and "<c>value is null</c>"
    /// compiles. <c>ImmutableArray&lt;T&gt;</c> cannot be compared to <c>null</c> at all (CS0037) --
    /// its null-ish state is <c>default(ImmutableArray&lt;T&gt;).IsDefault</c>, a distinct state from
    /// <c>ImmutableArray&lt;T&gt;.Empty</c> (which is NOT default). See the design note above
    /// <see cref="EmitWriteCollectionBody"/> for the write/read/size handling this drives.
    /// </summary>
    private static bool IsStructCollectionKind(FieldKind kind)
        => kind == FieldKind.ImmutableArray;

    private static string DefaultValue(FieldInfo field)
    {
        if (field.IsNullable)
            return "null";

        return field.Mapping.Kind switch
        {
            FieldKind.String => "null",
            FieldKind.ByteArray => "null",
            FieldKind.Int32 => "0",
            FieldKind.Int64 => "0L",
            FieldKind.Boolean => "false",
            FieldKind.Double => "0.0",
            FieldKind.Decimal => "0m",
            FieldKind.ActorRef => "global::Akka.Actor.ActorRefs.NoSender",
            FieldKind.EnvelopePayload => "null",
            FieldKind.Union => "null",
            // A required (non-nullable) [AkkaSerializable] struct nested field gets a non-nullable
            // local (see GenerateReadMessage's local declaration/IsReferenceLike): "null" would not
            // compile for it, so fall back to "default" the same way every other non-reference-like
            // kind does below.
            FieldKind.Object => IsReferenceLike(field) ? "null" : "default",
            _ => "default"
        };
    }

    private static bool IsRequired(FieldInfo field)
    {
        return !field.IsNullable;
    }

    private static bool IsReferenceLike(FieldInfo field)
    {
        // ImmutableArray<T> is a collection kind but a VALUE type (struct): a required field is
        // handled like any other non-nullable struct kind (Guid, DateTime, ...) below -- only the
        // "has this field index been seen" guard applies, never a "target is null" check (which
        // would not compile for a struct). See IsStructCollectionKind and the design note above
        // EmitWriteCollectionBody for the full default/IsDefault-vs-null story.
        if (IsStructCollectionKind(field.Mapping.Kind))
            return false;

        if (IsCollectionKind(field.Mapping.Kind))
            return true;

        if (field.Mapping.Kind == FieldKind.Formatted)
            return field.Formatter is { IsTargetValueType: false };

        // Mirrors the Formatted case above: an [AkkaSerializable] nested type used as a required
        // field can be a value type (a readonly record struct), in which case it behaves like a
        // scalar (non-nullable local/constructor argument, no null-check) rather than a reference.
        if (field.Mapping.Kind == FieldKind.Object)
            return !field.Mapping.IsValueType;

        // Union fields are always reference-like: the static type is an interface or abstract base
        // (a struct cannot be the static type of a multi-member union).
        return field.Mapping.Kind is FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef or FieldKind.EnvelopePayload or FieldKind.Union;
    }

    private static bool IsNullableValueField(FieldInfo field)
    {
        return field.IsNullable && !IsReferenceLike(field);
    }

    /// <summary>
    /// "__has" prefix (not the field's own camelCase local, which lacks it): guarantees no collision
    /// with an unrelated property's OWN value local under the pigeon-hole pairing this field name
    /// with another property's name, for example fields "Foo" and "HasFoo" -- "Foo"'s has-guard is
    /// "__hasFoo", distinct from "HasFoo"'s value local "hasFoo".
    /// </summary>
    private static Local GetHasLocal(FieldInfo field)
    {
        return Local.Reserved("__has" + field.Name);
    }

    private static ValueExpr GetFieldValueExpression(FieldInfo field)
    {
        ValueExpr name = Local.ForField(field.Name);
        return IsRequired(field) && IsReferenceLike(field) ? name.NullForgiven() : name;
    }

    private static string GetObjectMethodName(TypeMapping mapping)
    {
        return FoldTypeName(mapping.TypeFullName);
    }

    /// <summary>
    /// Folds a fully-qualified type name into a compact generated-member identifier the way
    /// System.Text.Json's <c>GetTypeInfoPropertyName</c> does: namespaces are dropped, each type
    /// identifier keeps only its simple name, and generic type arguments are concatenated --
    /// <c>Ns.Wrapper&lt;Ns.OrderRequest&gt;</c> becomes <c>WrapperOrderRequest</c>.
    /// These names appear in stack traces (WriteWrapperOrderRequest), so compactness matters.
    /// Flattening is collision-prone by construction (same simple name in two namespaces, marker
    /// ambiguity); AKKASG024 detects collisions among generated members and fails compilation
    /// instead of silently emitting duplicates -- the same trade System.Text.Json makes with its
    /// DuplicateTypeName diagnostic.
    /// </summary>
    private static string FoldTypeName(string typeFullName)
    {
        var sb = new StringBuilder(typeFullName.Length);
        var segment = new StringBuilder();

        void FlushSegment()
        {
            if (segment.Length == 0)
                return;

            sb.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
                sb.Append(segment.ToString(1, segment.Length - 1));
            segment.Clear();
        }

        var source = typeFullName.Replace("global::", string.Empty);
        foreach (var ch in source)
        {
            switch (ch)
            {
                case '.':
                case '+':
                    // Keep only the last identifier of a dotted/nested chain: the segment
                    // accumulated so far was a namespace or containing type.
                    segment.Clear();
                    break;
                case '<':
                case '>':
                case ',':
                case ' ':
                    FlushSegment();
                    break;
                case '[':
                    FlushSegment();
                    break;
                case ']':
                    sb.Append("Array");
                    break;
                case '?':
                    FlushSegment();
                    sb.Append("Nullable");
                    break;
                default:
                    segment.Append(ch);
                    break;
            }
        }

        FlushSegment();
        return sb.ToString();
    }

    private static string GetFormatterFieldName(FormatterInfo formatter)
    {
        return "_akkaFormatter_" + SanitizeTypeName(formatter.TargetTypeFullName);
    }

    private static string SanitizeTypeName(string typeFullName)
    {
        // Used only for formatter field names, whose target types are validated non-generic
        // (AKKASG011) -- generated METHOD names go through FoldTypeName instead. Escape literal
        // underscores FIRST so sanitization is collision-free: 'My.Ns.Foo_Bar' -> 'My_Ns_Foo__Bar'
        // and 'My.Ns.Foo.Bar' -> 'My_Ns_Foo_Bar' stay distinct instead of both collapsing to
        // 'My_Ns_Foo_Bar' (duplicate generated members).
        return typeFullName
            .Replace("global::", string.Empty)
            .Replace("_", "__")
            .Replace(".", "_")
            .Replace("+", "_");
    }

    private static string GetMessageMethodName(MessageInfo message)
    {
        return FoldTypeName(message.FullyQualifiedName);
    }

    private static string GetAccessibilityKeyword(Accessibility accessibility)
    {
        return accessibility == Accessibility.Internal ? "internal" : "public";
    }

    // Text-shaping helpers (keyword escaping, camel-casing, string-literal escaping) live on
    // CodeWriter: every emission site reaches them through the writer's typed appends
    // (Identifier/StringLiteral/LiteralText) or the Local/ValueExpr factories, so they cannot be
    // skipped at an emission site.

    // ---------------------------------------------------------------------------------------------
    // Cached pipeline models.
    //
    // Every type below flows through cached incremental nodes (the ForAttributeWithMetadataName
    // transforms and their Collect()ed results), so each one must be (a) SYMBOL-FREE -- an ISymbol
    // never compares equal across compilations, so retaining one silently defeats caching for the
    // whole downstream pipeline -- and (b) VALUE-EQUATABLE, because the incremental engine decides
    // "unchanged" via EqualityComparer<T>.Default. ImmutableArray<T> fields get explicit sequence
    // comparison in each Equals: the struct's own Equals compares the underlying array REFERENCE.
    // ---------------------------------------------------------------------------------------------
}
