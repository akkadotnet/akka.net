// //-----------------------------------------------------------------------
// // <copyright file="HoconExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Data;
using System.Runtime.CompilerServices;
using Akka.Configuration;

namespace Akka.Persistence.Sql.Common.Extensions
{
    public static class IsolationLevelExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IsolationLevel GetIsolationLevel(this Config config, string key)
            => config.GetString(key).ToIsolationLevel();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IsolationLevel ToIsolationLevel(this string level)
            => level switch
            {
                null => IsolationLevel.Unspecified,
                "chaos" => IsolationLevel.Chaos,
                "read-committed" => IsolationLevel.ReadCommitted,
                "read-uncommitted" => IsolationLevel.ReadUncommitted,
                "repeatable-read" => IsolationLevel.RepeatableRead,
                "serializable" => IsolationLevel.Serializable,
                "snapshot" => IsolationLevel.Snapshot,
                "unspecified" => IsolationLevel.Unspecified,
                _ => throw new ConfigurationException(
                    "Unknown isolation-level value. Should be one of: chaos | read-committed | read-uncommitted | repeatable-read | serializable | snapshot | unspecified")
            };
        
    }
}