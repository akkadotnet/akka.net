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
        private static readonly Dictionary<string, Func<Config, Props>> Plugins = BuildPlugins();

        private static readonly Dictionary<string, Func<IStashOverflowStrategyConfigurator>> StashOverflowConfigurators = new(StringComparer.Ordinal)
        {
            [typeof(ThrowExceptionConfigurator).FullName!] = static () => new ThrowExceptionConfigurator(),
            [typeof(DiscardConfigurator).FullName!] = static () => new DiscardConfigurator(),
        };

        /// <summary>
        /// Each factory calls the constructor the reflection path picks: <c>(Config)</c> where the type has one,
        /// else the parameterless one.
        /// </summary>
        private static Dictionary<string, Func<Config, Props>> BuildPlugins()
        {
            var builtIn = new Dictionary<string, Func<Config, Props>>(StringComparer.Ordinal);

            Add(static _ => new MemoryJournal());
            Add(static _ => new SharedMemoryJournal());
            Add(static _ => new MemorySnapshotStore());
            Add(static _ => new LocalSnapshotStore());
            Add(static _ => new NoSnapshotStore());
            Add(static config => new PersistencePluginProxy(config));

            return builtIn;

            // typeof(TActor) is what keeps this trimmer-safe: the key comes from the type, so it can't drift out of step with it.
            void Add<[DynamicallyAccessedMembers(Props.ActorTypeMembers)] TActor>(Func<Config, TActor> factory)
                where TActor : ActorBase
            {
                builtIn[typeof(TActor).FullName!] = config => Props.CreateBy(new PluginActorProducer<TActor>(factory, config));
            }
        }

        public static bool TryCreatePluginProps(string? typeName, Config pluginConfig, [NotNullWhen(true)] out Props? props)
        {
            if (ToBuiltInName(typeName) is { } name && Plugins.TryGetValue(name, out var factory))
            {
                props = factory(pluginConfig);
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
