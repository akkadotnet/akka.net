//-----------------------------------------------------------------------
// <copyright file="GoldenBytes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit.Sdk;

namespace Akka.Serialization
{
    /// <summary>
    /// Checked-in wire bytes: one <c>&lt;name&gt;.hex</c> file per message, 16 hex bytes to a row. <see cref="Check"/>
    /// compares and fails with the two byte rows side by side. Set <c>AKKA_GOLDEN_CAPTURE=1</c> to write the files
    /// instead, then review them with <c>git diff</c>.
    /// </summary>
    public sealed class GoldenBytes
    {
        /// <summary>Set this environment variable to <c>1</c> to write golden files instead of comparing them.</summary>
        public const string CaptureVariable = "AKKA_GOLDEN_CAPTURE";

        private const int BytesPerRow = 16;

        private readonly string _directory;
        private readonly bool _capture;

        /// <param name="directory">The folder that holds the files.</param>
        /// <param name="capture">True to write files instead of comparing.</param>
        public GoldenBytes(string directory, bool capture)
        {
            _directory = directory;
            _capture = capture;
        }

        /// <summary>
        /// The folder <paramref name="relativeDirectory"/> next to the source file that calls this, so a spec reads
        /// and writes the working tree it was built from. Capture follows <see cref="CaptureVariable"/>.
        /// </summary>
        public static GoldenBytes For(string relativeDirectory, [CallerFilePath] string sourceFile = "")
            => new(Path.Combine(Path.GetDirectoryName(sourceFile) ?? "", relativeDirectory),
                Environment.GetEnvironmentVariable(CaptureVariable) == "1");

        /// <summary>
        /// Compares <paramref name="actual"/> with the file <paramref name="name"/> (<c>/</c> makes sub-folders), or
        /// writes the file when capturing.
        /// </summary>
        /// <exception cref="XunitException">The file is missing or differs.</exception>
        public void Check(string name, byte[] actual)
        {
            var path = Path.Combine(_directory, name + ".hex");
            if (_capture)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Join("\n", Rows(actual)) + "\n", new UTF8Encoding(false));
                return;
            }

            var hint = $"To regenerate, run with {CaptureVariable}=1 and review the file in git.";
            if (!File.Exists(path))
                throw new XunitException($"No golden file {path}. {hint}");

            var expected = Parse(File.ReadAllText(path));
            if (expected.AsSpan().SequenceEqual(actual))
                return;

            throw new XunitException($"Golden bytes changed: {path}\n{Diff(expected, actual)}\n{hint}");
        }

        private static byte[] Parse(string text)
            => text.Split('\n').SelectMany(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();

        private static string[] Rows(byte[] bytes)
            => bytes.Select((b, i) => (b, i)).GroupBy(x => x.i / BytesPerRow)
                .Select(g => string.Join(" ", g.Select(x => x.b.ToString("x2", CultureInfo.InvariantCulture)))).ToArray();

        // differing rows are marked with "!"
        private static string Diff(byte[] expected, byte[] actual)
        {
            var left = Rows(expected);
            var right = Rows(actual);
            var sb = new StringBuilder($"expected {expected.Length} bytes, actual {actual.Length} bytes:\n");
            for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                var l = i < left.Length ? left[i] : "";
                var r = i < right.Length ? right[i] : "";
                sb.Append(l == r ? "  " : "! ").Append(l.PadRight(BytesPerRow * 3 - 1)).Append(" | ").Append(r).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }
    }
}
