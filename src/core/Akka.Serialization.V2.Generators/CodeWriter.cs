//-----------------------------------------------------------------------
// <copyright file="CodeWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Akka.Serialization.V2.Generators;

/// <summary>
/// Structured emission layer for <see cref="AkkaSerializerGenerator"/>: a fluent writer over a
/// <see cref="StringBuilder"/> with indent-level and fresh-line tracking, plus ref-struct scoped
/// builders (<see cref="BlockScope"/>, <see cref="IndentScope"/>, <see cref="SwitchScope"/>,
/// <see cref="InlineBraceScope"/>) that use C# 8 pattern-based dispose so <c>using</c> blocks
/// structurally enforce brace pairing and indentation.
/// </summary>
/// <remarks>
/// The writer bakes the generator's emission rules into its API so they cannot be skipped:
/// <list type="number">
/// <item><description>
/// IDENTIFIERS derived from user code only append through an escaping path:
/// <see cref="Identifier(string)"/> (and <see cref="ValueExpr.Member"/>/<see cref="Local.ForField"/>,
/// which delegate to it) always apply <see cref="EscapeIfKeyword"/>. <see cref="Raw(string)"/> is
/// reserved for generator-owned constant fragments (keywords, punctuation, fixed member names) and
/// is the single glaringly-deliberate escape hatch.
/// </description></item>
/// <item><description>
/// GENERATED LOCALS are only mintable as <see cref="Local"/> values: through
/// <see cref="NameAlloc"/>, through the reserved <c>__</c> prefix
/// (<see cref="Local.Reserved"/>), through the camel-cased-and-escaped per-field form
/// (<see cref="Local.ForField"/>), or as an explicitly-declared generator-owned name in a helper
/// body that contains no user-derived identifiers (<see cref="Local.GeneratorOwned"/>).
/// </description></item>
/// <item><description>
/// TYPE NAMES append through <see cref="Type(TypeName)"/>, which expects the fully-qualified
/// (<c>global::</c>) display form the pipeline models already carry (or a C# built-in keyword
/// form such as <c>int</c>/<c>string</c>/<c>int?</c>, which is what
/// <see cref="Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat"/> produces for
/// special types).
/// </description></item>
/// <item><description>
/// BLOCKS, SWITCHES, and IF-CHAIN bodies only come from the scoped builders: <see cref="Raw"/> and
/// <see cref="Line"/> reject <c>{</c>/<c>}</c> outside emitted string literals, so unbalanced
/// braces are unrepresentable.
/// </description></item>
/// </list>
/// The writer is emission-stage-only: it is never stored in a cached pipeline model, so it has no
/// bearing on incremental-caching equatability.
/// </remarks>
internal sealed class CodeWriter
{
    private const string IndentUnit = "    ";

    private readonly StringBuilder _sb;
    private int _indentLevel;
    private bool _atLineStart = true;

    public CodeWriter(StringBuilder sb, int indentLevel = 0)
    {
        _sb = sb;
        _indentLevel = indentLevel;
    }

    public int IndentLevel => _indentLevel;

    // ---------------------------------------------------------------------------------------
    // Raw path: generator-owned constant fragments ONLY (keywords, punctuation, operators,
    // fixed method/member names). Never pass user-derived identifiers, locals, or type names
    // through here -- use the typed appends below.
    // ---------------------------------------------------------------------------------------

    /// <summary>Appends a generator-owned constant fragment. Rejects newlines and structural braces (see class remarks, rule 4).</summary>
    public CodeWriter Raw(string text)
    {
        ValidateRaw(text);
        AppendUnchecked(text);
        return this;
    }

    /// <summary>Appends <paramref name="text"/> like <see cref="Raw"/>, then ends the line.</summary>
    public CodeWriter Line(string text)
    {
        Raw(text);
        return NewLine();
    }

    /// <summary>Ends the current line.</summary>
    public CodeWriter NewLine()
    {
        _sb.AppendLine();
        _atLineStart = true;
        return this;
    }

    /// <summary>Emits a completely empty line (no indentation). Only legal at the start of a line.</summary>
    public CodeWriter BlankLine()
    {
        if (!_atLineStart)
            throw new InvalidOperationException("BlankLine() is only legal at the start of a line; end the current line first.");

        _sb.AppendLine();
        return this;
    }

    // ---------------------------------------------------------------------------------------
    // Typed appends: the sanctioned paths for user-derived identifiers, generated locals,
    // fully-qualified type names, string literals, and composed value expressions.
    // ---------------------------------------------------------------------------------------

    /// <summary>Appends a user-derived identifier, always applying <see cref="EscapeIfKeyword"/>.</summary>
    public CodeWriter Identifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Identifier must be non-empty.", nameof(name));

        AppendUnchecked(EscapeIfKeyword(name));
        return this;
    }

    /// <summary>Appends a generated local (see <see cref="Generators.Local"/> for the minting rules).</summary>
    public CodeWriter Local(Local local)
    {
        AppendUnchecked(local.Name);
        return this;
    }

    /// <summary>Appends a fully-qualified type name (see <see cref="TypeName.Global"/>).</summary>
    public CodeWriter Type(TypeName type)
    {
        AppendUnchecked(type.Text);
        return this;
    }

    /// <summary>Appends a composed value expression (see <see cref="ValueExpr"/>).</summary>
    public CodeWriter Value(ValueExpr expr)
    {
        AppendUnchecked(expr.Text);
        return this;
    }

    /// <summary>Appends a quoted C# string literal, escaping backslashes and quotes in <paramref name="value"/>.</summary>
    public CodeWriter StringLiteral(string value)
    {
        AppendUnchecked("\"");
        AppendUnchecked(Escape(value));
        AppendUnchecked("\"");
        return this;
    }

    /// <summary>
    /// Appends escaped text INSIDE an already-open emitted string literal (e.g. user type/member
    /// names interpolated into an exception message). Applies <see cref="Escape"/>; the surrounding
    /// quotes belong to the adjacent <see cref="Raw"/> fragments.
    /// </summary>
    public CodeWriter LiteralText(string value)
    {
        AppendUnchecked(Escape(value));
        return this;
    }

    /// <summary>Appends an integer literal.</summary>
    public CodeWriter Number(int value)
    {
        WriteIndentIfPending();
        _sb.Append(value);
        return this;
    }

    // ---------------------------------------------------------------------------------------
    // Scoped builders: the ONLY sources of structural braces and indentation changes.
    // ---------------------------------------------------------------------------------------

    /// <summary>Opens a brace block: writes <c>{</c>, indents; dispose unindents and writes <c>}</c>.</summary>
    public BlockScope Block() => new(this, "}");

    /// <summary>Opens an expression block (e.g. a switch expression): writes <c>{</c>, indents; dispose unindents and writes <c>};</c>.</summary>
    public BlockScope ExpressionBlock() => new(this, "};");

    /// <summary>Indents one level with no braces (single-statement <c>if</c>/<c>else</c> bodies); dispose unindents.</summary>
    public IndentScope Indented() => new(this);

    /// <summary>
    /// Opens a switch-statement body after the caller has written the <c>switch (...)</c> header
    /// line. Case labels/bodies come from the returned scope's <c>Case*</c>/<c>Default</c> methods.
    /// </summary>
    public SwitchScope Switch() => new(this);

    /// <summary>Opens an inline object-initializer brace pair on the current line: writes <c> { </c>; dispose writes <c> }</c>.</summary>
    public InlineBraceScope InlineBraces() => new(this);

    // ---------------------------------------------------------------------------------------
    // Text-shaping helpers shared with the emission code.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// '@'-escapes <paramref name="identifier"/> if it is a reserved C# keyword, a runtime no-op
    /// that makes the text legal as an identifier. The escaped form composes safely with appended
    /// suffixes ('@eventSize', '@eventBytes'): '@' is legal on any identifier, keyword or not.
    /// </summary>
    internal static string EscapeIfKeyword(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;
    }

    /// <summary>
    /// Camel-cases a property name for use as a generated local, escaping the result when it
    /// camel-cases into a reserved C# keyword ('Event' -> '@event').
    /// </summary>
    internal static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var name = char.ToLowerInvariant(value[0]) + value.Substring(1);
        return EscapeIfKeyword(name);
    }

    /// <summary>Escapes backslashes and double quotes for embedding in an emitted C# string literal.</summary>
    internal static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ---------------------------------------------------------------------------------------
    // Internals.
    // ---------------------------------------------------------------------------------------

    private void AppendUnchecked(string text)
    {
        WriteIndentIfPending();
        _sb.Append(text);
    }

    private void WriteIndentIfPending()
    {
        if (!_atLineStart)
            return;

        for (var i = 0; i < _indentLevel; i++)
            _sb.Append(IndentUnit);
        _atLineStart = false;
    }

    internal void OpenScope()
    {
        if (!_atLineStart)
            throw new InvalidOperationException("A scope can only open at the start of a line; end the current line first.");

        AppendUnchecked("{");
        NewLine();
        _indentLevel++;
    }

    internal void CloseScope(string closer)
    {
        if (!_atLineStart)
            throw new InvalidOperationException("A scope can only close at the start of a line; end the current line first.");
        if (_indentLevel == 0)
            throw new InvalidOperationException("Unbalanced scope: indent level is already zero.");

        _indentLevel--;
        AppendUnchecked(closer);
        NewLine();
    }

    internal void OpenInline()
    {
        if (_atLineStart)
            throw new InvalidOperationException("Inline braces are only legal mid-line (after the expression they attach to).");

        _sb.Append(" { ");
    }

    internal void CloseInline()
    {
        if (_atLineStart)
            throw new InvalidOperationException("Inline braces must close on the line they were opened on.");

        _sb.Append(" }");
    }

    internal void PushIndent() => _indentLevel++;

    internal void PopIndent()
    {
        if (_indentLevel == 0)
            throw new InvalidOperationException("Unbalanced indent: indent level is already zero.");

        _indentLevel--;
    }

    /// <summary>
    /// Rejects newlines outright and rejects <c>{</c>/<c>}</c> outside emitted string literals, so
    /// structural braces can only come from the scoped builders. The scan tracks double-quoted
    /// spans (honoring backslash escapes), which keeps braces inside emitted literals -- e.g.
    /// interpolation holes in an emitted <c>$"..."</c> -- legal.
    /// </summary>
    private static void ValidateRaw(string text)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
                throw new ArgumentException($"Raw text must not contain newlines; use Line()/NewLine(). Text: [{text}]");

            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '{' || c == '}')
            {
                throw new ArgumentException($"Raw text must not contain structural braces; use Block()/ExpressionBlock()/InlineBraces(). Text: [{text}]");
            }
        }
    }
}

/// <summary>
/// A generated local variable name. Locals are only mintable through <see cref="NameAlloc"/>
/// (collision-free <c>__</c>-prefixed temporaries), <see cref="Reserved"/> (fixed generator-owned
/// locals under the reserved <c>__</c> prefix), <see cref="ForField"/> (the camel-cased, escaped
/// per-field value local), or <see cref="GeneratorOwned"/> (fixed locals in helper bodies that
/// contain no user-derived identifiers) -- never from an arbitrary raw string at an emission site.
/// </summary>
internal readonly struct Local
{
    public string Name { get; }

    private Local(string name)
    {
        Name = name;
    }

    /// <summary>
    /// A generator-owned local under the reserved <c>__</c> prefix, which cannot collide with any
    /// per-field local no matter what the [AkkaField] property is named.
    /// </summary>
    public static Local Reserved(string name)
    {
        if (!name.StartsWith("__", StringComparison.Ordinal))
            throw new ArgumentException($"Reserved locals must use the '__' prefix; got [{name}].", nameof(name));

        return new Local(name);
    }

    /// <summary>The per-field value local: the property name camel-cased and keyword-escaped.</summary>
    public static Local ForField(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Property name must be non-empty.", nameof(propertyName));

        return new Local(CodeWriter.ToCamelCase(propertyName));
    }

    /// <summary>
    /// A fixed generator-owned local WITHOUT the <c>__</c> prefix. Only legal in generated method
    /// bodies that never declare user-derived locals (e.g. the union dispatch helpers), where no
    /// collision is possible. The name must be a valid non-keyword identifier.
    /// </summary>
    public static Local GeneratorOwned(string name)
    {
        ValidateSimpleIdentifier(name);
        return new Local(name);
    }

    /// <summary>
    /// Appends an identifier-safe suffix ('Size', 'Bytes'). Safe on an escaped base: '@' is legal
    /// on any identifier, so '@event' + 'Size' -> '@eventSize'.
    /// </summary>
    public Local WithSuffix(string suffix)
    {
        ValidateSimpleIdentifier(suffix);
        return new Local(Name + suffix);
    }

    private static void ValidateSimpleIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Identifier must be non-empty.", nameof(name));

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var valid = c == '_' || char.IsLetter(c) || (i > 0 && char.IsDigit(c));
            if (!valid)
                throw new ArgumentException($"[{name}] is not a valid identifier fragment.", nameof(name));
        }

        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            throw new ArgumentException($"[{name}] is a C# keyword; generator-owned locals must not need escaping.", nameof(name));
    }

    public override string ToString() => Name;
}

/// <summary>Allocates collision-free local names within a single generated method body.</summary>
internal sealed class NameAlloc
{
    private int _counter;

    public Local Next(string hint) => Local.Reserved("__" + hint + _counter++);
}

/// <summary>
/// A C# type name in the fully-qualified display form the pipeline models carry
/// (<see cref="Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat"/>): either
/// <c>global::</c>-qualified, or a built-in keyword form (<c>int</c>, <c>string</c>, <c>int?</c>,
/// <c>int[]</c>, ...), which is what that format produces for special types.
/// </summary>
internal readonly struct TypeName
{
    public string Text { get; }

    private TypeName(string text)
    {
        Text = text;
    }

    public static TypeName Global(string fullyQualifiedName)
    {
        if (string.IsNullOrEmpty(fullyQualifiedName))
            throw new ArgumentException("Type name must be non-empty.", nameof(fullyQualifiedName));

        if (!fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal) && !char.IsLower(fullyQualifiedName[0]))
            throw new ArgumentException($"Type name [{fullyQualifiedName}] is neither global::-qualified nor a built-in keyword form.", nameof(fullyQualifiedName));

        for (var i = 0; i < fullyQualifiedName.Length; i++)
        {
            var c = fullyQualifiedName[i];
            if (c is '{' or '}' or ';' or '\r' or '\n' or '"')
                throw new ArgumentException($"Type name [{fullyQualifiedName}] contains an illegal character.", nameof(fullyQualifiedName));
        }

        return new TypeName(fullyQualifiedName);
    }

    public override string ToString() => Text;
}

/// <summary>
/// A composed value expression (e.g. <c>message.Event</c>, <c>__kvp3.Key</c>,
/// <c>@event.Value</c>). Built only from typed parts: a <see cref="Local"/>, a generator-owned
/// root, and member/null-forgiving composition -- member names go through
/// <see cref="CodeWriter.EscapeIfKeyword"/> like every other identifier.
/// </summary>
internal readonly struct ValueExpr
{
    public string Text { get; }

    private ValueExpr(string text)
    {
        Text = text;
    }

    /// <summary>A generator-owned root expression (a generated parameter such as <c>message</c> or <c>value</c>).</summary>
    public static ValueExpr GeneratorOwned(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Expression root must be non-empty.", nameof(name));

        return new ValueExpr(name);
    }

    public static implicit operator ValueExpr(Local local) => new(local.Name);

    /// <summary>Member access; the member name is keyword-escaped like any identifier.</summary>
    public ValueExpr Member(string name) => new(Text + "." + CodeWriter.EscapeIfKeyword(name));

    /// <summary>Appends the null-forgiving operator (a runtime no-op used to store nullable read temporaries into non-nullable slots).</summary>
    public ValueExpr NullForgiven() => new(Text + "!");

    public override string ToString() => Text;
}

/// <summary>
/// Brace-block scope: created via <see cref="CodeWriter.Block"/> /
/// <see cref="CodeWriter.ExpressionBlock"/>; writes <c>{</c> and indents on creation, unindents
/// and writes the closer (<c>}</c> or <c>};</c>) on dispose. Being a ref struct, it cannot
/// escape the stack, and the paired <c>using</c> makes an unbalanced brace unrepresentable.
/// </summary>
internal readonly ref struct BlockScope
{
    private readonly CodeWriter _writer;
    private readonly string _closer;

    internal BlockScope(CodeWriter writer, string closer)
    {
        _writer = writer;
        _closer = closer;
        writer.OpenScope();
    }

    public void Dispose() => _writer.CloseScope(_closer);
}

/// <summary>Pure indentation scope for brace-less single-statement bodies.</summary>
internal readonly ref struct IndentScope
{
    private readonly CodeWriter _writer;

    internal IndentScope(CodeWriter writer)
    {
        _writer = writer;
        writer.PushIndent();
    }

    public void Dispose() => _writer.PopIndent();
}

/// <summary>
/// Switch-statement scope: opens the switch body's brace block; case labels are written through
/// the <c>Case*</c>/<c>Default</c> methods, each returning a <see cref="CaseScope"/> that indents
/// the case body and unindents on dispose. The caller writes <c>break;</c>/<c>return</c>/throw
/// terminators explicitly, exactly as the emitted code requires.
/// </summary>
internal readonly ref struct SwitchScope
{
    private readonly CodeWriter _writer;

    internal SwitchScope(CodeWriter writer)
    {
        _writer = writer;
        writer.OpenScope();
    }

    public CaseScope CaseNumber(int value)
    {
        _writer.Raw("case ").Number(value).Line(":");
        return new CaseScope(_writer);
    }

    public CaseScope CaseStringLiteral(string value)
    {
        _writer.Raw("case ").StringLiteral(value).Line(":");
        return new CaseScope(_writer);
    }

    public CaseScope CaseNull()
    {
        _writer.Line("case null:");
        return new CaseScope(_writer);
    }

    public CaseScope CaseTypePattern(TypeName type, string bindingName)
    {
        _writer.Raw("case ").Type(type).Raw(" ").Raw(bindingName).Line(":");
        return new CaseScope(_writer);
    }

    public CaseScope Default()
    {
        _writer.Line("default:");
        return new CaseScope(_writer);
    }

    public void Dispose() => _writer.CloseScope("}");
}

/// <summary>Case-body scope produced by <see cref="SwitchScope"/>: indented on creation, unindented on dispose.</summary>
internal readonly ref struct CaseScope
{
    private readonly CodeWriter _writer;

    internal CaseScope(CodeWriter writer)
    {
        _writer = writer;
        writer.PushIndent();
    }

    public void Dispose() => _writer.PopIndent();
}

/// <summary>
/// Inline brace pair on a single line (object initializers): writes <c> { </c> on creation and
/// <c> }</c> on dispose.
/// </summary>
internal readonly ref struct InlineBraceScope
{
    private readonly CodeWriter _writer;

    internal InlineBraceScope(CodeWriter writer)
    {
        _writer = writer;
        writer.OpenInline();
    }

    public void Dispose() => _writer.CloseInline();
}
