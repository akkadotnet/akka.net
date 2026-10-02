//-----------------------------------------------------------------------
// <copyright file="GoldenBytes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit.Sdk;

namespace Akka.Serialization
{
    /// <summary>Which wire a <see cref="GoldenBytes"/> folder pins down.</summary>
    public enum GoldenKind
    {
        /// <summary>
        /// Bytes the legacy serializer wrote. A porter captures these before the port touches anything, so they
        /// are the contract the V2 node has to keep reading.
        /// </summary>
        Legacy,

        /// <summary>Bytes the V2 serializer writes. Any change to them is a wire format change.</summary>
        V2
    }

    /// <summary>
    /// A folder of checked-in golden files, one <c>&lt;name&gt;.hex</c> per message: a few <c>#</c> comment lines, then
    /// the bytes as hex, 16 to a row. <see cref="Check"/> fails with a side-by-side dump of the first difference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Capturing.</b> Set <c>AKKA_GOLDEN_CAPTURE</c> to <c>legacy</c>, <c>v2</c> or <c>all</c> and run the spec.
    /// A folder of that kind writes its files instead of comparing, and the spec passes. Review the files with
    /// <c>git diff</c>; an ordinary run (variable unset, as on CI) only ever compares. A porter captures the
    /// <c>legacy</c> folder first, from the legacy serializer alone, in the commit before the port, and the
    /// <c>v2</c> folder once the V2 serializer works.
    /// </para>
    /// <para>
    /// The files live next to the spec's source. <see cref="For"/> finds that folder through
    /// <see cref="CallerFilePathAttribute"/>, so a spec reads and writes the working tree it was compiled from.
    /// </para>
    /// </remarks>
    public sealed class GoldenBytes
    {
        /// <summary>The environment variable that switches checks to capturing: <c>legacy</c>, <c>v2</c> or <c>all</c>.</summary>
        public const string CaptureVariable = "AKKA_GOLDEN_CAPTURE";

        private const int BytesPerRow = 16;
        private const int MaxRowsInDiff = 24;

        /// <param name="directory">The folder holding the files.</param>
        /// <param name="capture">True to write files instead of comparing them.</param>
        /// <param name="kind">Used in messages, and by <see cref="V2PortSpecs"/> to say what is being pinned.</param>
        public GoldenBytes(string directory, bool capture, GoldenKind kind)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Capture = capture;
            Kind = kind;
        }

        /// <summary>The folder holding the files.</summary>
        public string Directory { get; }

        /// <summary>True when <see cref="Check"/> writes the files rather than comparing them.</summary>
        public bool Capture { get; }

        /// <summary>Whether this folder pins legacy or V2 bytes.</summary>
        public GoldenKind Kind { get; }

        /// <summary>
        /// The folder <c>&lt;folder of the calling source file&gt;/&lt;relativeDirectory&gt;/legacy</c> or <c>/v2</c>.
        /// Capturing follows <see cref="CaptureVariable"/>.
        /// </summary>
        /// <param name="relativeDirectory">Such as <c>GoldenBytes/ReliableDelivery</c>.</param>
        /// <param name="kind">Which of the two sub-folders.</param>
        /// <param name="sourceFile">Filled in by the compiler: the file that calls this.</param>
        public static GoldenBytes For(string relativeDirectory, GoldenKind kind, [CallerFilePath] string sourceFile = "")
        {
            var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? string.Empty;
            var relative = relativeDirectory.Replace('/', Path.DirectorySeparatorChar);
            var directory = Path.Combine(sourceDirectory, relative, kind == GoldenKind.Legacy ? "legacy" : "v2");
            return new GoldenBytes(directory, CaptureRequested(kind), kind);
        }

        /// <summary>Whether <see cref="CaptureVariable"/> asks to capture <paramref name="kind"/>.</summary>
        public static bool CaptureRequested(GoldenKind kind)
        {
            var value = Environment.GetEnvironmentVariable(CaptureVariable)?.Trim().ToLowerInvariant();
            return value switch
            {
                "all" or "1" or "true" => true,
                "legacy" => kind == GoldenKind.Legacy,
                "v2" => kind == GoldenKind.V2,
                _ => false
            };
        }

        /// <summary>The path of the file for <paramref name="name"/>.</summary>
        public string PathFor(string name) => Path.Combine(Directory, V2PortCase.Sanitize(name) + ".hex");

        /// <summary>True when a golden file for <paramref name="name"/> exists.</summary>
        public bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>
        /// Compares <paramref name="actual"/> with the golden file, or writes the file when <see cref="Capture"/> is set.
        /// </summary>
        /// <param name="name">The case name.</param>
        /// <param name="actual">The bytes a serializer wrote.</param>
        /// <param name="description">Comment lines for the top of a captured file: manifest, serializer id, message type.</param>
        /// <exception cref="XunitException">The file is missing or differs.</exception>
        public void Check(string name, byte[] actual, params string[] description)
        {
            if (Capture)
            {
                Write(name, actual, description);
                return;
            }

            var path = PathFor(name);
            if (!File.Exists(path))
                throw new XunitException(
                    $"No {Kind} golden file for '{name}': expected {path}.{Environment.NewLine}" +
                    $"To create it, run this spec once with {CaptureVariable}={KindWord(Kind)} and review the file in git.");

            var expected = Parse(File.ReadAllText(path));
            if (expected.AsSpan().SequenceEqual(actual))
                return;

            throw new XunitException(
                $"{Kind} golden bytes for '{name}' changed ({path}).{Environment.NewLine}" +
                Diff(expected, actual) + Environment.NewLine +
                $"If the change is intended, recapture with {CaptureVariable}={KindWord(Kind)} and review the file in git.");
        }

        /// <summary>The bytes of the golden file for <paramref name="name"/>.</summary>
        /// <exception cref="XunitException">The file is missing.</exception>
        public byte[] Read(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path))
                throw new XunitException(
                    $"No {Kind} golden file for '{name}': expected {path}.{Environment.NewLine}" +
                    $"Capture the legacy bytes with {CaptureVariable}=legacy before changing the serializer, then check the files in.");

            return Parse(File.ReadAllText(path));
        }

        /// <summary>Writes <paramref name="bytes"/> to the golden file for <paramref name="name"/>.</summary>
        public void Write(string name, byte[] bytes, params string[] description)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var sb = new StringBuilder();
            sb.Append("# Akka.NET golden bytes, ").Append(Kind).Append(" wire. Recapture: ")
                .Append(CaptureVariable).Append('=').Append(KindWord(Kind)).Append(" dotnet test ...\n");
            sb.Append("# case: ").Append(name).Append('\n');
            foreach (var line in description)
                sb.Append("# ").Append(line.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            sb.Append(Dump(bytes, includeOffsets: true));
            File.WriteAllText(PathFor(name), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>The bytes of a golden file's text: comments and offsets are ignored, so any line ending works.</summary>
        public static byte[] Parse(string text)
        {
            var bytes = new List<byte>();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var colon = line.IndexOf(':');
                if (colon >= 0)
                    line = line.Substring(colon + 1);

                foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
                        throw new FormatException($"'{token}' is not a hex byte in golden file line: {rawLine.Trim()}");
                    bytes.Add(b);
                }
            }

            return bytes.ToArray();
        }

        /// <summary>The hex dump a golden file holds: 16 bytes per row, each row led by its offset.</summary>
        public static string Dump(byte[] bytes, bool includeOffsets)
        {
            var sb = new StringBuilder();
            for (var offset = 0; offset < bytes.Length; offset += BytesPerRow)
            {
                if (includeOffsets)
                    sb.Append(offset.ToString("x4", CultureInfo.InvariantCulture)).Append(": ");
                sb.Append(Row(bytes, offset)).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// A readable account of how two byte arrays differ: the lengths, the first differing offset, and the rows around
        /// it side by side, differing rows marked.
        /// </summary>
        public static string Diff(byte[] expected, byte[] actual)
        {
            var common = Math.Min(expected.Length, actual.Length);
            var first = 0;
            while (first < common && expected[first] == actual[first])
                first++;

            var sb = new StringBuilder();
            sb.Append("  expected ").Append(expected.Length).Append(" bytes, actual ").Append(actual.Length).Append(" bytes; ");
            if (first < common)
                sb.Append("first difference at offset ").Append(first).Append(" (expected 0x")
                    .Append(expected[first].ToString("x2", CultureInfo.InvariantCulture)).Append(", actual 0x")
                    .Append(actual[first].ToString("x2", CultureInfo.InvariantCulture)).Append(").\n");
            else
                sb.Append("one is a prefix of the other, they part at offset ").Append(first).Append(".\n");

            var rows = (Math.Max(expected.Length, actual.Length) + BytesPerRow - 1) / BytesPerRow;
            var firstRow = first / BytesPerRow;
            var from = rows <= MaxRowsInDiff ? 0 : Math.Max(0, firstRow - MaxRowsInDiff / 4);
            var to = rows <= MaxRowsInDiff ? rows : Math.Min(rows, from + MaxRowsInDiff);

            var width = BytesPerRow * 3 - 1;
            sb.Append("  offset   ").Append("expected".PadRight(width)).Append("   actual\n");
            if (from > 0)
                sb.Append("  ...\n");
            for (var row = from; row < to; row++)
            {
                var offset = row * BytesPerRow;
                var left = Row(expected, offset);
                var right = Row(actual, offset);
                sb.Append(left == right ? "   " : " ! ")
                    .Append(offset.ToString("x4", CultureInfo.InvariantCulture)).Append("    ")
                    .Append(left.PadRight(width)).Append(" | ").Append(right).Append('\n');
            }

            if (to < rows)
                sb.Append("  ...\n");
            return sb.ToString().TrimEnd('\n');
        }

        private static string Row(byte[] bytes, int offset)
        {
            if (offset >= bytes.Length)
                return string.Empty;

            return string.Join(" ", bytes.Skip(offset).Take(BytesPerRow)
                .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string KindWord(GoldenKind kind) => kind == GoldenKind.Legacy ? "legacy" : "v2";
    }
}
