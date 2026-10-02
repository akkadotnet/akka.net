//-----------------------------------------------------------------------
// <copyright file="GoldenBaseline.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Akka.Serialization.V2.Tests.Harness;

/// <summary>
/// Strict byte-for-byte comparison of one generated file against its checked-in baseline under
/// <c>GoldenOutput/</c> beside the calling spec file, the same convention <c>GeneratorGoldenOutputSpec</c> uses: no scrubbing, no
/// newline normalization, and <c>*.received.txt</c> written next to the baseline on a mismatch.
/// Run with <c>AKKA_GOLDEN_REGEN=1</c> to (re)capture a baseline deliberately, then review the diff.
/// </summary>
internal static class GoldenBaseline
{
    private const string RegenEnvVar = "AKKA_GOLDEN_REGEN";

    /// <returns>Null when <paramref name="actual"/> matches its baseline; otherwise a failure description.</returns>
    public static string? Compare(string hintName, string actual, [CallerFilePath] string callerFilePath = "")
    {
        var goldenDirectory = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "GoldenOutput");
        var verifiedPath = Path.GetFullPath(Path.Combine(goldenDirectory, hintName + ".verified.txt"));

        if (Environment.GetEnvironmentVariable(RegenEnvVar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(verifiedPath)!);
            File.WriteAllText(verifiedPath, actual);
            return null;
        }

        if (!File.Exists(verifiedPath))
            return $"Missing baseline [{verifiedPath}]. Run once with {RegenEnvVar}=1 to capture it, then review and check it in.";

        var expected = File.ReadAllText(verifiedPath);
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return null;

        var receivedPath = Path.Combine(Path.GetDirectoryName(verifiedPath)!, hintName + ".received.txt");
        File.WriteAllText(receivedPath, actual);
        return $"Generated output for [{hintName}] differs from baseline. {DescribeFirstDifference(expected, actual)} Actual output written to [{receivedPath}] for diffing against [{verifiedPath}].";
    }

    private static string DescribeFirstDifference(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);
        var index = 0;
        while (index < length && expected[index] == actual[index])
            index++;

        if (index == length && expected.Length == actual.Length)
            return "Contents differ but no differing index was found (unexpected).";

        string Snippet(string text)
        {
            var start = Math.Max(0, index - 40);
            return text.Substring(start, Math.Min(80, text.Length - start)).Replace("\r", "\\r").Replace("\n", "\\n");
        }

        return $"First difference at char index {index}: expected [...{Snippet(expected)}...] but got [...{Snippet(actual)}...].";
    }
}
