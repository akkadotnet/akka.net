//-----------------------------------------------------------------------
// <copyright file="BuiltInPersistencePlugins.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.Util;

namespace Akka.Persistence
{
    /// <summary>
    /// INTERNAL API
    ///
    /// The types Akka.Persistence ships, so HOCON can name them with <c>Akka.DynamicTypeLoading</c> off.
    /// Keyed by the bare full name; a HOCON value is normalized first, so no assembly, or the
    /// Akka.Persistence assembly in any casing, resolves here and any other assembly does not.
    /// </summary>
    internal static class BuiltInPersistencePlugins
    {
        // The same records a registration in code makes, so a built-in and a registered plugin start through one path:
        // CreateProps. A built-in is found by the type name HOCON gives it, and HOCON can mount it under any plugin id,
        // so it is keyed by that type's full name and not by an id. That is why the record's PluginId holds the full
        // name here. These records are never added to the registry, which is keyed by id.
        private static readonly Dictionary<string, PersistenceActorPluginDetails> Plugins = BuildPlugins();

        private static readonly Dictionary<string, Func<IStashOverflowStrategyConfigurator>> StashOverflowConfigurators = new(StringComparer.Ordinal)
        {
            [typeof(ThrowExceptionConfigurator).FullName!] = static () => new ThrowExceptionConfigurator(),
            [typeof(DiscardConfigurator).FullName!] = static () => new DiscardConfigurator(),
        };

        /// <summary>
        /// Each factory calls the constructor the reflection path picks: <c>(Config)</c> where the type has one,
        /// else the parameterless one.
        /// </summary>
        private static Dictionary<string, PersistenceActorPluginDetails> BuildPlugins()
        {
            // typeof(T).FullName keys each record, so the key can't drift out of step with the type.
            // PersistencePluginProxy serves as both a journal and a snapshot store; the record kind plays no part here.
            var builtIn = new PersistenceActorPluginDetails[]
            {
                JournalDetails.Create(typeof(MemoryJournal).FullName!, static _ => new MemoryJournal()),
                JournalDetails.Create(typeof(SharedMemoryJournal).FullName!, static _ => new SharedMemoryJournal()),
                SnapshotStoreDetails.Create(typeof(MemorySnapshotStore).FullName!, static _ => new MemorySnapshotStore()),
                SnapshotStoreDetails.Create(typeof(LocalSnapshotStore).FullName!, static _ => new LocalSnapshotStore()),
                SnapshotStoreDetails.Create(typeof(NoSnapshotStore).FullName!, static _ => new NoSnapshotStore()),
                JournalDetails.Create(typeof(PersistencePluginProxy).FullName!, static config => new PersistencePluginProxy(config)),
            };

            var byName = new Dictionary<string, PersistenceActorPluginDetails>(StringComparer.Ordinal);
            foreach (var details in builtIn)
                byName[details.PluginId] = details;
            return byName;
        }

        public static bool TryCreatePluginProps(string? typeName, Config pluginConfig, [NotNullWhen(true)] out Props? props)
        {
            if (ToBuiltInName(typeName) is { } name && Plugins.TryGetValue(name, out var details))
            {
                props = details.CreateProps(pluginConfig);
                return true;
            }

            props = null;
            return false;
        }

        public static bool TryCreateStashOverflowConfigurator(string? typeName, [NotNullWhen(true)] out IStashOverflowStrategyConfigurator? configurator)
        {
            if (ToBuiltInName(typeName) is { } name && StashOverflowConfigurators.TryGetValue(name, out var factory))
            {
                configurator = factory();
                return true;
            }

            configurator = null;
            return false;
        }

        private static string? ToBuiltInName(string? typeName)
        {
            if (!TypeExtensions.TrySplitTypeName(typeName, out var name, out var assembly))
                return null;

            return assembly is null || string.Equals(assembly, "Akka.Persistence", StringComparison.OrdinalIgnoreCase)
                ? name
                : null;
        }
    }
}
