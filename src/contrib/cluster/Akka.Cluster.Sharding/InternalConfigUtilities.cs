// -----------------------------------------------------------------------
//  <copyright file="InternalConfigUtilities.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Cluster.Sharding;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class InternalConfigUtilities
{
    /// <summary>
    /// Null in this case means "off"
    /// </summary>
    /// <param name="coordinatorHocon">The HOCON config used by a shard coordinator</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static LogLevel? ParseVerboseLogSettings(Config coordinatorHocon)
    {
        var logLevel = coordinatorHocon.GetString("akka.cluster.sharding.verbose-debug-logging");
        return logLevel switch
        {
            "off" => null,
            "on" => LogLevel.DebugLevel,
            "error" => LogLevel.ErrorLevel,
            "warning" => LogLevel.WarningLevel,
            "info" => LogLevel.InfoLevel,
            "debug" => LogLevel.DebugLevel,
            _ => throw new ArgumentException($"Unknown log level: {logLevel}")
        };
    }
}