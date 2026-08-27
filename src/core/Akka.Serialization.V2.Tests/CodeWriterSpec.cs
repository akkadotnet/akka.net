//-----------------------------------------------------------------------
// <copyright file="CodeWriterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Unit tests for the <see cref="CodeWriter"/> emission layer: indentation correctness,
/// scope-based brace pairing, the identifier/local/type-name invariants, and escaping.
/// </summary>
public sealed class CodeWriterSpec
{
    // CodeWriter emits LF unconditionally (platform-invariant output; golden baselines are LF-pinned).
    private const string NL = "\n";

    private static (CodeWriter Writer, StringBuilder Sb) Create(int indentLevel = 0)
    {
        var sb = new StringBuilder();
        return (new CodeWriter(sb, indentLevel), sb);
    }

    [Fact(DisplayName = "Line should write the tracked indent before its text")]
    public void Should_WriteIndent_When_LineStartsAtTrackedLevel()
    {
        var (w, sb) = Create(indentLevel: 2);
        w.Line("var x = 1;");
        sb.ToString().Should().Be("        var x = 1;" + NL);
    }

    [Fact(DisplayName = "Mid-line appends should not re-emit indentation")]
    public void Should_NotReindent_When_AppendingMidLine()
    {
        var (w, sb) = Create(indentLevel: 1);
        w.Raw("size += SizeOfInt32(").Number(42).Line(");");
        sb.ToString().Should().Be("    size += SizeOfInt32(42);" + NL);
    }

    [Fact(DisplayName = "Block should open a brace, indent its body, and close on dispose")]
    public void Should_PairBraces_When_BlockScopeDisposed()
    {
        var (w, sb) = Create(indentLevel: 1);
        using (w.Block())
        {
            w.Line("inner();");
            using (w.Block())
                w.Line("nested();");
        }

        sb.ToString().Should().Be(
            "    {" + NL +
            "        inner();" + NL +
            "        {" + NL +
            "            nested();" + NL +
            "        }" + NL +
            "    }" + NL);
    }

    [Fact(DisplayName = "ExpressionBlock should close with '};'")]
    public void Should_CloseWithSemicolon_When_ExpressionBlockDisposed()
    {
        var (w, sb) = Create();
        w.Line("return obj switch");
        using (w.ExpressionBlock())
            w.Line("_ => 1");

        sb.ToString().Should().Be(
            "return obj switch" + NL +
            "{" + NL +
            "    _ => 1" + NL +
            "};" + NL);
    }

    [Fact(DisplayName = "Indented should indent brace-less bodies and restore the level on dispose")]
    public void Should_RestoreIndent_When_IndentScopeDisposed()
    {
        var (w, sb) = Create();
        w.Line("if (x is null)");
        using (w.Indented())
            w.Line("writer.WriteNil();");
        w.Line("else");
        using (w.Indented())
            w.Line("writer.Write(x);");

        sb.ToString().Should().Be(
            "if (x is null)" + NL +
            "    writer.WriteNil();" + NL +
            "else" + NL +
            "    writer.Write(x);" + NL);
    }

    [Fact(DisplayName = "BlankLine should emit a completely empty line with no indentation")]
    public void Should_EmitNoIndent_When_BlankLineInsideIndentedScope()
    {
        var (w, sb) = Create(indentLevel: 3);
        w.Line("a();");
        w.BlankLine();
        w.Line("b();");
        sb.ToString().Should().Be("            a();" + NL + NL + "            b();" + NL);
    }

    [Fact(DisplayName = "BlankLine should reject being called mid-line")]
    public void Should_Throw_When_BlankLineCalledMidLine()
    {
        var (w, _) = Create();
        w.Raw("dangling");
        var act = () => w.BlankLine();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "Switch scope should indent case labels and bodies correctly")]
    public void Should_LayoutCases_When_SwitchScopeUsed()
    {
        var (w, sb) = Create(indentLevel: 1);
        w.Line("switch (__fieldId)");
        using (var sw = w.Switch())
        {
            using (sw.CaseNumber(1))
            {
                w.Line("x = reader.ReadInt32();");
                w.Line("break;");
            }

            using (sw.CaseStringLiteral("a\\b"))
                w.Line("break;");

            using (sw.CaseNull())
                w.Line("break;");

            using (sw.Default())
                w.Line("break;");
        }

        sb.ToString().Should().Be(
            "    switch (__fieldId)" + NL +
            "    {" + NL +
            "        case 1:" + NL +
            "            x = reader.ReadInt32();" + NL +
            "            break;" + NL +
            "        case \"a\\\\b\":" + NL +
            "            break;" + NL +
            "        case null:" + NL +
            "            break;" + NL +
            "        default:" + NL +
            "            break;" + NL +
            "    }" + NL);
    }

    [Fact(DisplayName = "InlineBraces should wrap an object initializer on one line")]
    public void Should_WrapInitializer_When_InlineBracesUsed()
    {
        var (w, sb) = Create();
        w.Raw("return new Foo()");
        using (w.InlineBraces())
            w.Raw("A = a, B = b");
        w.Line(";");

        sb.ToString().Should().Be("return new Foo() { A = a, B = b };" + NL);
    }

    [Fact(DisplayName = "Raw should reject structural braces outside emitted string literals")]
    public void Should_Throw_When_RawContainsStructuralBraces()
    {
        var (w, _) = Create();
        var open = () => w.Raw("if (x) {");
        var close = () => w.Raw("}");
        open.Should().Throw<ArgumentException>();
        close.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Raw should allow braces inside emitted string literals (interpolation holes)")]
    public void Should_AllowBraces_When_InsideEmittedStringLiteral()
    {
        var (w, sb) = Create();
        w.Line("throw new global::System.ArgumentException($\"Unsupported type: {obj.GetType()}\", nameof(obj));");
        sb.ToString().Should().Contain("{obj.GetType()}");
    }

    [Fact(DisplayName = "Raw should honor backslash escapes when tracking emitted string literals")]
    public void Should_TrackEscapedQuotes_When_ValidatingRawText()
    {
        var (w, _) = Create();
        // The emitted literal ends at the SECOND quote; the escaped \" does not terminate it,
        // so the brace after it is still inside the literal and legal.
        var act = () => w.Raw("writer.Write(\"a\\\"{b}\");");
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Raw should reject newlines")]
    public void Should_Throw_When_RawContainsNewline()
    {
        var (w, _) = Create();
        var act = () => w.Raw("a;\nb;");
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Identifier should keyword-escape user-derived identifiers")]
    public void Should_EscapeKeyword_When_IdentifierIsReservedWord()
    {
        var (w, sb) = Create();
        w.Identifier("event").Raw(": ").Identifier("Event");
        sb.ToString().Should().Be("@event: Event");
    }

    [Fact(DisplayName = "StringLiteral should escape backslashes and quotes")]
    public void Should_EscapeContent_When_StringLiteralWritten()
    {
        var (w, sb) = Create();
        w.StringLiteral("esc-\"quote\"-\\-v1");
        sb.ToString().Should().Be("\"esc-\\\"quote\\\"-\\\\-v1\"");
    }

    [Fact(DisplayName = "Local.ForField should camel-case and keyword-escape, and compose suffixes safely")]
    public void Should_CamelCaseAndEscape_When_LocalMintedForField()
    {
        Local.ForField("Event").Name.Should().Be("@event");
        Local.ForField("OrderId").Name.Should().Be("orderId");
        Local.ForField("Event").WithSuffix("Size").Name.Should().Be("@eventSize");
        Local.ForField("Event").WithSuffix("Bytes").Name.Should().Be("@eventBytes");
    }

    [Fact(DisplayName = "Local.Reserved should require the '__' prefix")]
    public void Should_Throw_When_ReservedLocalLacksPrefix()
    {
        var act = () => Local.Reserved("fieldCount");
        act.Should().Throw<ArgumentException>();
        Local.Reserved("__fieldCount").Name.Should().Be("__fieldCount");
    }

    [Fact(DisplayName = "Local.GeneratorOwned should reject keywords and invalid identifiers")]
    public void Should_Throw_When_GeneratorOwnedLocalInvalid()
    {
        ((Action)(() => Local.GeneratorOwned("event"))).Should().Throw<ArgumentException>();
        ((Action)(() => Local.GeneratorOwned("1abc"))).Should().Throw<ArgumentException>();
        ((Action)(() => Local.GeneratorOwned("a.b"))).Should().Throw<ArgumentException>();
        Local.GeneratorOwned("runtimeType").Name.Should().Be("runtimeType");
    }

    [Fact(DisplayName = "NameAlloc should mint collision-free '__'-prefixed temporaries")]
    public void Should_MintSequentialNames_When_NameAllocUsed()
    {
        var alloc = new NameAlloc();
        alloc.Next("size").Name.Should().Be("__size0");
        alloc.Next("item").Name.Should().Be("__item1");
        alloc.Next("size").Name.Should().Be("__size2");
    }

    [Fact(DisplayName = "TypeName.Global should accept global:: and keyword forms, and reject bare names")]
    public void Should_ValidateForms_When_TypeNameCreated()
    {
        TypeName.Global("global::Akka.Actor.IActorRef").Text.Should().Be("global::Akka.Actor.IActorRef");
        TypeName.Global("int?").Text.Should().Be("int?");
        TypeName.Global("string").Text.Should().Be("string");
        TypeName.Global("int[][]").Text.Should().Be("int[][]");
        ((Action)(() => TypeName.Global("Akka.Actor.IActorRef"))).Should().Throw<ArgumentException>();
        ((Action)(() => TypeName.Global("global::Foo;Bar"))).Should().Throw<ArgumentException>();
        ((Action)(() => TypeName.Global(""))).Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "ValueExpr should compose member access through the escaping path")]
    public void Should_EscapeMembers_When_ValueExprComposed()
    {
        var message = ValueExpr.GeneratorOwned("message");
        message.Member("OrderId").Text.Should().Be("message.OrderId");
        message.Member("Value").Text.Should().Be("message.Value");
        ((ValueExpr)Local.ForField("Event")).NullForgiven().Text.Should().Be("@event!");
    }

    [Fact(DisplayName = "CloseScope should reject closing mid-line or below level zero")]
    public void Should_Throw_When_ScopesUnbalanced()
    {
        var (w, _) = Create();
        var act = () => w.CloseScopeForTest();
        act.Should().Throw<InvalidOperationException>();
    }
}

/// <summary>Test-only access to internal scope plumbing (guard-rail assertions).</summary>
internal static class CodeWriterTestExtensions
{
    public static void CloseScopeForTest(this CodeWriter writer) => writer.CloseScope("}");
}
