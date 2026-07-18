//-----------------------------------------------------------------------
// <copyright file="HexDumpFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Text;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Renders a wire-format snapshot as a human-reviewable annotated hex dump: a header block
/// (case name, CLR message type, manifest, serializer id, byte count) followed by a
/// classic <c>offset | hex | ascii</c> hex dump body. Used exclusively by
/// <see cref="WireFormatSnapshotSpec"/> to produce the committed <c>WireSnapshots/*.verified.txt</c>
/// artifacts -- NOT raw <c>.bin</c> files -- so a wire-format change shows up as a reviewable text
/// diff in a pull request instead of an opaque binary diff.
/// </summary>
internal static class HexDumpFormatter
{
    private const int BytesPerRow = 16;

    public static string Format(string caseName, string messageType, string manifest, int serializerId, byte[] bytes)
    {
        var builder = new StringBuilder();
        builder.Append("case: ").Append(caseName).Append('\n');
        builder.Append("message-type: ").Append(messageType).Append('\n');
        builder.Append("manifest: ").Append(manifest).Append('\n');
        builder.Append("serializer-id: ").Append(serializerId).Append('\n');
        builder.Append("byte-count: ").Append(bytes.Length).Append('\n');
        builder.Append('\n');

        if (bytes.Length == 0)
        {
            builder.Append("(zero-byte payload)\n");
            return builder.ToString();
        }

        for (var offset = 0; offset < bytes.Length; offset += BytesPerRow)
        {
            var rowLength = Math.Min(BytesPerRow, bytes.Length - offset);
            AppendRow(builder, bytes, offset, rowLength);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, byte[] bytes, int offset, int rowLength)
    {
        builder.Append(offset.ToString("x4")).Append("  ");

        for (var column = 0; column < BytesPerRow; column++)
        {
            if (column < rowLength)
                builder.Append(bytes[offset + column].ToString("x2")).Append(' ');
            else
                builder.Append("   ");

            if (column == BytesPerRow / 2 - 1)
                builder.Append(' ');
        }

        builder.Append(" |");
        for (var column = 0; column < rowLength; column++)
        {
            var value = bytes[offset + column];
            builder.Append(value is >= 0x20 and <= 0x7e ? (char)value : '.');
        }

        builder.Append('|').Append('\n');
    }

    /// <summary>
    /// Formats a (possibly closed-generic) CLR type as a readable, language-shaped name for the
    /// snapshot header -- for example <c>Wrapper&lt;OrderRequest&gt;</c> instead of
    /// <see cref="Type.Name"/>'s raw <c>Wrapper`1</c>.
    /// </summary>
    public static string FriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex > 0)
            name = name[..backtickIndex];

        var typeArguments = string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName));
        return $"{name}<{typeArguments}>";
    }
}
