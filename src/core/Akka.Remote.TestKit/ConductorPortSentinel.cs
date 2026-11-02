//-----------------------------------------------------------------------
// <copyright file="ConductorPortSentinel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Globalization;
using Akka.Annotations;

namespace Akka.Remote.TestKit;

/// <summary>
/// INTERNAL API.
///
/// Contract between a conductor node and the multi-node test runner that started it.
/// The conductor binds its <see cref="Controller"/> socket, then writes one sentinel line to
/// stdout naming the port it got. The runner reads that line and starts the remaining nodes
/// with that port.
///
/// The runner cannot pick the port itself. Probing for a free port and handing it over leaves a
/// window in which any other socket on the machine can take that port before the conductor binds.
/// </summary>
[InternalApi]
public static class ConductorPortSentinel
{
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    /// <summary>
    /// Leading text of the sentinel line. Distinctive so it cannot collide with spec output.
    /// </summary>
    public const string Prefix = "[MULTINODE-CONDUCTOR-PORT]";

    /// <summary>
    /// Renders the sentinel line for a bound conductor port.
    /// </summary>
    /// <param name="port">Port the conductor bound to. Must be in [1, 65535].</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="port"/> is out of range.</exception>
    public static string Format(int port)
    {
        if (port < MinPort || port > MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port), port,
                $"Conductor port must be in [{MinPort}, {MaxPort}].");

        return Prefix + port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a conductor port out of one line of node output.
    /// </summary>
    /// <param name="line">A single line of node stdout or stderr. May be <c>null</c>.</param>
    /// <param name="port">Receives the parsed port, or <c>0</c> when the line is not a sentinel.</param>
    /// <returns><c>true</c> when the line is a well formed sentinel.</returns>
    public static bool TryParse(string? line, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line!.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        // NumberStyles.None rejects signs, decimal points and embedded whitespace.
        var value = trimmed.Substring(Prefix.Length);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (parsed < MinPort || parsed > MaxPort)
            return false;

        port = parsed;
        return true;
    }
}
