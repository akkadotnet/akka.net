//-----------------------------------------------------------------------
// <copyright file="PersistencePluginRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Util;

namespace Akka.Persistence
{
    /// <summary>
    /// INTERNAL API
    ///
    /// The one place persistence (and Akka.Persistence.Query) reads code-based registrations from, one per
    /// actor system. Today the only source is the user's <see cref="PersistenceSetup"/>; a module source can
    /// plug in here later without touching the lookup sites. Answers the registration arm only: each site
    /// writes the built-in, guard and reflection arms out itself, see <see cref="AkkaFeatures"/>.
    /// </summary>
    internal sealed class PersistencePluginRegistry
    {
        private static readonly ConditionalWeakTable<ActorSystem, PersistencePluginRegistry> Registries = new();

        private static readonly IReadOnlyList<EventAdapterDetails> NoAdapters = Array.Empty<EventAdapterDetails>();

        private readonly Dictionary<string, PersistencePluginDetails> _plugins;
        private readonly Dictionary<string, List<EventAdapterDetails>> _adapters;

        private PersistencePluginRegistry(
            Dictionary<string, PersistencePluginDetails> plugins,
            Dictionary<string, List<EventAdapterDetails>> adapters)
        {
            _plugins = plugins;
            _adapters = adapters;
        }

        /// <summary>
        /// The registry of <paramref name="system"/>, built from its <see cref="PersistenceSetup"/> on first use.
        /// </summary>
        public static PersistencePluginRegistry For(ExtendedActorSystem system)
            => Registries.GetValue(system, static s => Build(((ExtendedActorSystem)s).Settings.Setup.Get<PersistenceSetup>().GetOrElse(null!)));

        /// <summary>
        /// Merges the registrations of <paramref name="setup"/>, in order. Per plugin id the later registration
        /// wins the factory and the default config. Event adapters of a journal accumulate over all registrations
        /// for its id, and the later one wins a name clash.
        /// </summary>
        public static PersistencePluginRegistry Build(PersistenceSetup? setup)
        {
            var plugins = new Dictionary<string, PersistencePluginDetails>(StringComparer.Ordinal);
            var adapters = new Dictionary<string, List<EventAdapterDetails>>(StringComparer.Ordinal);
            if (setup is null)
                return new PersistencePluginRegistry(plugins, adapters);

            foreach (var registration in setup.Registrations)
            {
                if (registration.Plugin is { } plugin)
                {
                    plugins[plugin.PluginId] = plugin;
                    if (plugin is JournalDetails journal)
                    {
                        foreach (var adapter in journal.EventAdapters)
                            AddAdapter(adapters, journal.PluginId, adapter);
                    }
                }
                else
                {
                    AddAdapter(adapters, registration.JournalPluginId!, registration.EventAdapter!);
                }
            }

            return new PersistencePluginRegistry(plugins, adapters);
        }

        private static void AddAdapter(Dictionary<string, List<EventAdapterDetails>> adapters, string journalPluginId, EventAdapterDetails adapter)
        {
            if (!adapters.TryGetValue(journalPluginId, out var list))
                adapters[journalPluginId] = list = new List<EventAdapterDetails>();
            list.Add(adapter);
        }

        /// <summary>
        /// The event adapters registered for the journal at <paramref name="journalPluginId"/>, in registration
        /// order. An adapter name that repeats keeps the later one.
        /// </summary>
        public IReadOnlyList<EventAdapterDetails> EventAdaptersFor(string journalPluginId)
        {
            if (!_adapters.TryGetValue(journalPluginId, out var added))
                return NoAdapters;

            var byName = new Dictionary<string, EventAdapterDetails>(StringComparer.Ordinal);
            foreach (var adapter in added)
                byName[adapter.Name] = adapter;
            return byName.Values.ToList();
        }

        /// <summary>
        /// Finds the registered plugin at <paramref name="pluginId"/> if it is a <typeparamref name="T"/>.
        /// </summary>
        public bool TryGet<T>(string? pluginId, [NotNullWhen(true)] out T? details) where T : PersistencePluginDetails
        {
            if (pluginId is not null && _plugins.TryGetValue(pluginId, out var found) && found is T typed)
            {
                details = typed;
                return true;
            }

            details = null;
            return false;
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Builds a plugin actor from a factory, so Props never needs reflection to find a constructor.
    /// </summary>
    internal sealed class PluginActorProducer<[DynamicallyAccessedMembers(Props.ActorTypeMembers)] TActor> : IIndirectActorProducer
        where TActor : ActorBase
    {
        private readonly Func<Config, TActor> _factory;
        private readonly Config _config;

        public PluginActorProducer(Func<Config, TActor> factory, Config config)
        {
            _factory = factory;
            _config = config;
        }

        [DynamicallyAccessedMembers(Props.ActorTypeMembers)]
        public Type ActorType => typeof(TActor);

        public ActorBase Produce() => _factory(_config);

        public void Release(ActorBase actor)
        {
        }
    }
}
