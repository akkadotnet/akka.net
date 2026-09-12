//-----------------------------------------------------------------------
// <copyright file="CrossAssemblyBaselineSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// These tests pin what <see cref="AkkaSerializerGenerator"/> does today when a serializable type
/// lives in a referenced assembly, not in the compilation that hosts the serializer. They check
/// real behavior, not ideal behavior. See design.md Decision 13.
/// </summary>
/// <remarks>
/// The harness builds a small "assembly A" from source into an in-memory
/// <see cref="MetadataReference"/>. Assembly A never runs the generator. It only supplies types.
/// The harness then builds "assembly B", which references A and runs the generator, using the
/// same base references as <see cref="AkkaSerializerGeneratorDiagnosticsSpec"/>.
/// </remarks>
public sealed class CrossAssemblyBaselineSpec
{
    [Fact(DisplayName = "Cross-assembly baseline: a nested field type declared and [AkkaSerializable] in a referenced assembly now compiles clean, its schema read straight from A's metadata")]
    public void Nested_field_type_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case1.AssemblyA;

            [AkkaSerializable]
            public sealed record Money([property: AkkaField(1)] long Cents);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case1.AssemblyA;

            namespace CrossAssemblyBaseline.Case1.AssemblyB;

            public interface IShop { }

            [AkkaSerializable(Manifest = "pay-v1")]
            public sealed record Pay([property: AkkaField(1)] Money Amount) : IShop;

            [AkkaSerializer<IShop>("shop", 130001)]
            public sealed partial class ShopSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case1.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case1.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Why this case now compiles clean (Decision 16).
        //
        //   Assembly A (referenced)             Assembly B (this compilation)
        //   +-------------------------+         +-------------------------------+
        //   | [AkkaSerializable]      |         | [AkkaSerializable] Pay        |
        //   | record Money(...)       | <------ |   [AkkaField(1)] Money Amount |
        //   +-------------------------+ property+-------------------------------+
        //            ^ type symbol                        ^ syntax tree
        //            |                                    |
        //   Money maps to FieldKind.Object,        Pay.Amount's Object mapping names Money.
        //   with ForeignAssemblyName set to A.      messagesByType (local + closed-generic
        //                                            schemas) has no entry for it.
        //                     |                                |
        //                     +---------------+----------------+
        //                                     v
        //             ComputeMetadataSchemas (a per-compilation stage, before
        //             validation runs): resolves Money's SYMBOL from A's metadata,
        //             checks it is accessible and [AkkaSerializable], then runs it
        //             through the SAME ExtractMessageCore local types use. The
        //             result is merged into the message table Pay.Amount looks up
        //             against, so the lookup that used to miss now hits.
        //
        // Before this decision, Money was not in ANY table this compilation built from syntax,
        // so this case failed -- AKKASG007 (previously AKKASG023, a mislabel already fixed on
        // an earlier branch; see design.md Decision 16's own characterization notes). Now the
        // metadata schema fills that gap: no diagnostic fires, and ShopSerializer emits
        // WriteMoney/ReadMoney helpers built from A's metadata, byte-identical to what A's own
        // generator run would have produced for the same declaration.
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        all.Should().NotContain(d => d.Id == "AKKASG007" || d.Id == "AKKASG023" || d.Id == "AKKASG039");
        generatedSource.Should().Contain("WriteMoney").And.Contain("ReadMoney");
    }

    [Fact(DisplayName = "Cross-assembly baseline: union members declared and [AkkaSerializable] in a referenced assembly fail AKKASG015 (not found), even though the type-level [AkkaUnion] declaration on the interface IS discovered")]
    public void Union_members_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case2.AssemblyA;

            [AkkaUnion(typeof(Placed), typeof(Cancelled))]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record Placed([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record Cancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case2.AssemblyA;

            namespace CrossAssemblyBaseline.Case2.AssemblyB;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "order-notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrders;

            [AkkaSerializer<IOrders>("orders", 130002)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case2.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case2.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Why this case now compiles clean (Decision 16).
        // The generator reads [AkkaUnion] off the IOrderEvent symbol. This already worked across
        // assemblies before this decision: the field maps to FieldKind.Union, not Unsupported, so
        // AKKASG003 never fires. What used to fail is the SAME lookup case 1 hit: each member
        // (Placed, Cancelled) is checked against messagesByType, which held only types declared
        // here. Both live in A, so both used to fail as AKKASG015, even though both already carry
        // [AkkaSerializable] and a manifest there.
        //
        // The fix is the same metadata-schema stage case 1 uses, applied to a union member instead
        // of a nested field: ComputeMetadataSchemas resolves Placed's and Cancelled's symbols from
        // A's metadata, confirms each is accessible and [AkkaSerializable], and extracts each
        // through ExtractMessageCore -- the schema ValidateUnionField's messagesByType lookup now
        // finds. No diagnostic fires, and OrdersSerializer emits a union dispatch helper that
        // writes/reads both members exactly as it would if Placed and Cancelled were declared
        // locally.
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        all.Should().NotContain(d => d.Id == "AKKASG003" || d.Id == "AKKASG015" || d.Id == "AKKASG039");
        generatedSource.Should().Contain("WritePlaced").And.Contain("WriteCancelled");
    }

    [Fact(DisplayName = "Cross-assembly baseline: a generic [AkkaSerializable] definition from a referenced assembly compiles clean and emits Wrapper<int> helpers when closed-generic-registered in B, but still fails AKKASG023 when it is not")]
    public void Generic_definition_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case3.AssemblyA;

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Body);
            """;

        const string sourceBRegistered = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case3.AssemblyA;

            namespace CrossAssemblyBaseline.Case3.AssemblyB;

            public interface IWrap { }

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Wrapper<int> Count) : IWrap;

            [AkkaSerializer<IWrap>("wrap", 130003)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrap-int-v1")]
            public sealed partial class WrapSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case3.A");
        var (registeredGeneratorDiagnostics, registeredCompileDiagnostics, generatedSource) =
            RunGeneratorAgainstB(sourceBRegistered, assemblyA, "CrossAssemblyBaseline.Case3.Registered");
        var registeredAll = registeredGeneratorDiagnostics.AddRange(registeredCompileDiagnostics);

        // Why registering the closed construction works.
        // [AkkaSerializable<Wrapper<int>>] reads the Wrapper<T> definition off the type symbol,
        // not off syntax. The registration step adds Wrapper<int> to messagesByType itself.
        // This avoids the gap that cases 1 and 2 hit. No errors. The generator emits the write
        // and read helpers.
        registeredAll.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("WriteWrapperInt");
        generatedSource.Should().Contain("ReadWrapperInt");

        // Same setup as above, but with no [AkkaSerializable<Wrapper<int>>] registration.
        const string sourceBUnregistered = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case3.AssemblyA;

            namespace CrossAssemblyBaseline.Case3.AssemblyB;

            public interface IWrap { }

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Wrapper<int> Count) : IWrap;

            [AkkaSerializer<IWrap>("wrap", 130004)]
            public sealed partial class WrapSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var (unregisteredGeneratorDiagnostics, unregisteredCompileDiagnostics, _) =
            RunGeneratorAgainstB(sourceBUnregistered, assemblyA, "CrossAssemblyBaseline.Case3.Unregistered");
        var unregisteredAll = unregisteredGeneratorDiagnostics.AddRange(unregisteredCompileDiagnostics);

        // Without the registration, Wrapper<int> is missing from messagesByType, and it is a
        // generic construction. So this reports AKKASG023, not AKKASG007.
        unregisteredAll.Should().Contain(d =>
            d.Id == "AKKASG023" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Wrapper", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Cross-assembly baseline: registering a closed generic construction of an unreachable, non-protocol referenced-assembly definition adopts it as its own top-level message (AKKASG034 retired, Decision 18)")]
    public void Unreachable_closed_generic_registration_of_customer_envelope()
    {
        // Before Decision 18 this exact shape -- the customer's own motivating case, Envelope<T> in
        // a referenced assembly, registered for a construction that implements no protocol and is
        // reachable from no field -- was AKKASG034, and it suppressed emission of the WHOLE
        // serializer. Decision 18 retires that check: a registration on the serializer ADOPTS its
        // construction unconditionally, so Envelope<AcceptCassette> becomes CommsSerializer's own
        // top-level message, dispatched by "env-dmac", with a concrete typeof() binding of its own
        // (Envelope<T> cannot implement IComms across the assembly boundary, so the runtime binding
        // lookup needs that extra binding to route it at all).
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case4.AssemblyA;

            [AkkaSerializable]
            public sealed record Envelope<T>(
                [property: AkkaField(1)] T Message,
                [property: AkkaField(2)] string TraceId);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case4.AssemblyA;

            namespace CrossAssemblyBaseline.Case4.AssemblyB;

            public interface IComms { }

            [AkkaSerializable(Manifest = "dmac")]
            public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : IComms;

            [AkkaSerializer<IComms>("comms", 130005)]
            [AkkaSerializable<Envelope<AcceptCassette>>(Manifest = "env-dmac")]
            public sealed partial class CommsSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case4.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case4.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        generatedSource.Should().NotBeEmpty("Decision 18 adopts the construction instead of suppressing the whole serializer");
        generatedSource.Should().Contain("env-dmac");
        generatedSource.Should().Contain("typeof(global::CrossAssemblyBaseline.Case4.AssemblyA.Envelope<global::CrossAssemblyBaseline.Case4.AssemblyB.AcceptCassette>)");
    }

    [Fact(DisplayName = "Cross-assembly baseline: a generic property substituted to object, through a referenced-assembly definition and made reachable, is still recognized as an envelope payload")]
    public void Envelope_payload_on_generic_property_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case5.AssemblyA;

            [AkkaSerializable]
            public sealed record Envelope<T>(
                [property: AkkaField(1)] T Message,
                [property: AkkaField(2)] string TraceId);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case5.AssemblyA;

            namespace CrossAssemblyBaseline.Case5.AssemblyB;

            public interface IComms { }

            [AkkaSerializable(Manifest = "dmac")]
            public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : IComms;

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Envelope<object> Inner) : IComms;

            [AkkaSerializer<IComms>("comms", 130006)]
            [AkkaSerializable<Envelope<object>>(Manifest = "env-any")]
            public sealed partial class CommsSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case5.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case5.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Why this field is an envelope payload, not AKKASG003.
        // Envelope<object>.Message is a substituted member. After substitution its static type
        // is System.Object, no matter which assembly declared the generic definition. No
        // attribute is declared anywhere. So the generator treats the field as an envelope
        // payload from the type alone. Without this, T substituted to an interface would be
        // unsupported.
        all.Should().NotContain(d => d.Id == "AKKASG003");
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("WriteEnvelopePayload");
    }

    [Fact(DisplayName = "Cross-assembly baseline: a protocol implementor declared only in a referenced assembly is now discovered by Decision 19's referenced-assembly walk, and gets a Manifest dispatch arm")]
    public void Protocol_implementor_declared_only_in_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case6.AssemblyA;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrders;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case6.AssemblyA;

            namespace CrossAssemblyBaseline.Case6.AssemblyB;

            [AkkaSerializer<IOrders>("orders", 130007)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case6.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case6.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Why this case now compiles clean, with a dispatch arm (Decision 19).
        // The generator walks every referenced assembly that itself references
        // Akka.Serialization.V2 (CrossAssemblyBaseline.Case6.A qualifies), enumerating its public
        // types from metadata and adopting every [AkkaSerializable] implementor of OrdersSerializer's
        // protocol it finds -- OrderPlaced, even though it is declared only in A. Its full schema is
        // extracted the same way a locally-named nested field's foreign type already was (Decision
        // 16's ComputeMetadataSchemas, now also seeded from this walk), so OrdersSerializer gets a
        // real WriteOrderPlaced/ReadOrderPlaced pair, a "placed-v1" Manifest dispatch arm, and a
        // typeof(OrderPlaced) binding -- byte-identical to what a local declaration would produce.
        all.Should().NotContain(d => d.Id == "AKKASG029");
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("OrderPlaced");
        generatedSource.Should().Contain("placed-v1");
        generatedSource.Should().Contain("WriteOrderPlaced").And.Contain("ReadOrderPlaced");
    }

    // ------------------------------------------------------------------------------------------
    // AKKASG039 (Decision 16): a referenced-assembly type carries [AkkaSerializable], but this
    // compilation cannot see it or one of its members. New cases, added alongside the six
    // characterization cases above rather than folded into them -- AKKASG039 did not exist before
    // this decision, so there is no "before" behavior to pin.
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "AKKASG039: a nested field type whose own [AkkaField] property has an internal-only getter (no InternalsVisibleTo) fails AKKASG039 naming that property, not just the type")]
    public void Nested_field_type_with_inaccessible_property()
    {
        // Money itself is public (B's own source must be able to name it at all), and so is the
        // Secret PROPERTY -- but its getter specifically is internal to A. Roslyn's metadata importer
        // still surfaces the property itself (public overall), but its getter is invisible to a
        // compilation A has not granted InternalsVisibleTo to: IPropertySymbol.GetMethod resolves to
        // null. ExtractMessageCore already treats a null getter as "no accessible getter"
        // (InvalidFieldInfo, the same path AKKASG028 uses for a local static/inaccessible property),
        // so Money's metadata schema comes back with a non-empty InvalidFields -- exactly the shape
        // ComputeMetadataSchemas treats as an accessibility failure. (A property whose OWN
        // accessibility, not just its getter's, is too low is invisible to symbol.GetMembers()
        // entirely and cannot be diagnosed this way at all -- see ComputeMetadataSchemas's own
        // comment on that Roslyn-level limit.)
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case7.AssemblyA;

            [AkkaSerializable]
            public sealed class Money
            {
                [AkkaField(1)]
                public long Cents { get; init; }

                [AkkaField(2)]
                public string Secret { internal get; init; } = string.Empty;
            }
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case7.AssemblyA;

            namespace CrossAssemblyBaseline.Case7.AssemblyB;

            public interface IShop { }

            [AkkaSerializable(Manifest = "pay-v1")]
            public sealed record Pay([property: AkkaField(1)] Money Amount) : IShop;

            [AkkaSerializer<IShop>("shop", 130008)]
            public sealed partial class ShopSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case7.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case7.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        all.Should().NotContain(d => d.Id == "AKKASG007");
        all.Should().Contain(d =>
            d.Id == "AKKASG039" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Amount", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("Money", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("CrossAssemblyBaseline.Case7.A", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("Secret", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("accessible getter", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("ShopSerializer", StringComparison.Ordinal));
        generatedSource.Should().BeEmpty("AKKASG039 is an error, so the pipeline skips AddSource for ShopSerializer");
    }

    [Fact(DisplayName = "AKKASG039: a union member that is itself internal to the referenced assembly (declared via an [AkkaUnion] usage written IN that assembly) fails AKKASG039")]
    public void Union_member_type_not_accessible()
    {
        // [AkkaUnion(typeof(Placed), typeof(Cancelled))] is written IN assembly A, on IOrderEvent,
        // so the C# compiler only ever needs Cancelled to be accessible FROM A -- an internal type
        // is always accessible from its own assembly. B never names Cancelled directly; it only
        // names the public interface IOrderEvent. That is exactly the shape that lets a directly
        // inaccessible TYPE (not one level down, unlike the nested-field case above) reach this
        // generator at all: B's own source never had a chance to reject it at compile time.
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case8.AssemblyA;

            [AkkaUnion(typeof(Placed), typeof(Cancelled))]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record Placed([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            internal sealed record Cancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case8.AssemblyA;

            namespace CrossAssemblyBaseline.Case8.AssemblyB;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "order-notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrders;

            [AkkaSerializer<IOrders>("orders", 130009)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case8.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case8.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Placed resolves fine (public, accessible) -- only Cancelled fails.
        all.Should().NotContain(d => d.Id == "AKKASG015" && d.GetMessage(null).Contains("Placed", StringComparison.Ordinal));
        all.Should().Contain(d =>
            d.Id == "AKKASG039" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Cancelled", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("Event", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("CrossAssemblyBaseline.Case8.A", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("internal", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("OrdersSerializer", StringComparison.Ordinal));
        generatedSource.Should().BeEmpty("AKKASG039 is an error, so the pipeline skips AddSource for OrdersSerializer");
    }

    [Fact(DisplayName = "AKKASG039: an accessibility problem one level down (a nested field's OWN union member is internal) is attributed to the actual broken type, and still reports at the LOCAL top-level reference site")]
    public void Nested_field_failure_one_level_down()
    {
        // Money is fully public and would resolve fine on its own. Its own field, Location, is typed
        // as the PUBLIC interface IAddressKind -- Money never directly appears inaccessible. But
        // IAddressKind's only declared union member, SecretAddress, is internal to A. Building
        // Money's schema succeeds (Location itself is fully readable), but completing the union it
        // carries requires resolving SecretAddress's own schema, and THAT fails. Decision 16: the
        // failure "can sit one level down ... it does not stop at the property in B that referenced
        // it" -- the message must name SecretAddress, not just Money, while the diagnostic still
        // reports at Pay's own Amount property in B (the only local site there is).
        //
        // This is also the only way a directly-inaccessible NESTED type can reach this generator at
        // all in practice: a nested OBJECT field's own type must be exactly as accessible as the
        // field itself (C# CS0051/CS0053), so an object-typed field can never point at a strictly
        // less accessible nested type. A union field has no such constraint -- [AkkaUnion] is
        // written IN A, on IAddressKind, so SecretAddress only ever needs to be accessible from A's
        // own code, which internal always is.
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case9.AssemblyA;

            [AkkaUnion(typeof(SecretAddress))]
            public interface IAddressKind { }

            [AkkaSerializable(Manifest = "secret-address-v1")]
            internal sealed record SecretAddress([property: AkkaField(1)] string Street) : IAddressKind;

            [AkkaSerializable]
            public sealed record Money([property: AkkaField(1)] long Cents, [property: AkkaField(2)] IAddressKind Location);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case9.AssemblyA;

            namespace CrossAssemblyBaseline.Case9.AssemblyB;

            public interface IShop { }

            [AkkaSerializable(Manifest = "pay-v1")]
            public sealed record Pay([property: AkkaField(1)] Money Amount) : IShop;

            [AkkaSerializer<IShop>("shop", 130010)]
            public sealed partial class ShopSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case9.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case9.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        all.Should().Contain(d =>
            d.Id == "AKKASG039" &&
            d.Severity == DiagnosticSeverity.Error &&
            // Reports at Pay.Amount -- the LOCAL site that named Money -- even though Money itself
            // is accessible; there is no local site on SecretAddress to point at instead.
            d.GetMessage(null).Contains("Amount", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("Money", StringComparison.Ordinal) &&
            // The failing type named in the message is SecretAddress, one level below Money.
            d.GetMessage(null).Contains("SecretAddress", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("internal", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("ShopSerializer", StringComparison.Ordinal));
        generatedSource.Should().BeEmpty("AKKASG039 is an error, so the pipeline skips AddSource for ShopSerializer");
    }

    // Delegates to the shared harness (Harness/GeneratorTestHarness.cs), which builds the base
    // metadata reference set once per process instead of once per test.
    private static MetadataReference CompileAssemblyAToReference(string source, string assemblyName)
    {
        return GeneratorTestHarness.CompileToReference(source, assemblyName);
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileDiagnostics, string GeneratedSource) RunGeneratorAgainstB(
        string sourceB, MetadataReference assemblyAReference, string assemblyName)
    {
        // assemblyName previously named the "B" compilation itself, purely for debugging -- it
        // never appears in any assertion (only assembly A's name, baked into assemblyAReference by
        // CompileAssemblyAToReference above, does). The harness names every compilation it builds
        // uniformly, so assemblyName is unused here now.
        _ = assemblyName;

        var result = GeneratorTestHarness.Run(sourceB, assemblyAReference);
        var generatedSource = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        return (result.GeneratorDiagnostics, result.CompileDiagnostics, generatedSource);
    }
}
