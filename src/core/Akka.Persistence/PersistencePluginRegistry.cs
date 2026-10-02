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
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
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

        private readonly Dictionary<string, PersistencePluginDetails> _plugins;

        private PersistencePluginRegistry(Dictionary<string, PersistencePluginDetails> plugins, IStashOverflowStrategyConfigurator? stashOverflowConfigurator)
        {
            _plugins = plugins;
            StashOverflowConfigurator = stashOverflowConfigurator;
        }

        /// <summary>
        /// Replaces <c>akka.persistence.internal-stash-overflow-strategy</c> when set.
        /// </summary>
        public IStashOverflowStrategyConfigurator? StashOverflowConfigurator { get; }

        /// <summary>
        /// The registry of <paramref name="system"/>. The setup's plugin factory runs once, on first use.
        /// </summary>
        public static PersistencePluginRegistry For(ExtendedActorSystem system)
            => Registries.GetValue(system, static s => Create((ExtendedActorSystem)s));

        private static PersistencePluginRegistry Create(ExtendedActorSystem system)
        {
            var plugins = new Dictionary<string, PersistencePluginDetails>(StringComparer.Ordinal);
            var setup = system.Settings.Setup.Get<PersistenceSetup>().GetOrElse(null!);
            if (setup is null)
                return new PersistencePluginRegistry(plugins, null);

            foreach (var details in setup.CreatePlugins(system))
                plugins[details.PluginId] = details;

            return new PersistencePluginRegistry(plugins, setup.StashOverflowConfigurator);
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
