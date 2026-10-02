//-----------------------------------------------------------------------
// <copyright file="PersistencePluginRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
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
    /// The one place persistence reads code-based registrations from. Today the only source is the user's
    /// <see cref="PersistencePluginSetup"/>; a module source can plug in here later without touching the
    /// lookup sites. Answers the Setup arm only: each site writes the built-in, guard and reflection arms
    /// out itself, see <see cref="AkkaFeatures"/>.
    /// </summary>
    internal sealed class PersistencePluginRegistry
    {
        private readonly PersistencePluginSetup _setup;

        public PersistencePluginRegistry(PersistencePluginSetup? setup)
        {
            _setup = setup ?? PersistencePluginSetup.Empty;
        }

        public static PersistencePluginRegistry From(ActorSystemSetup setup)
            => new(setup.Get<PersistencePluginSetup>().GetOrElse(null!));

        public bool TryCreatePluginProps(string? typeName, Config pluginConfig, [NotNullWhen(true)] out Props? props)
        {
            foreach (var registration in _setup.PluginFactories)
            {
                if (TypeExtensions.MatchesTypeName(typeName, registration.Key))
                {
                    props = registration.Value(pluginConfig);
                    return true;
                }
            }

            props = null;
            return false;
        }

        public bool TryCreateEventAdapter(string? typeName, ExtendedActorSystem system, [NotNullWhen(true)] out IEventAdapter? adapter)
        {
            foreach (var registration in _setup.AdapterFactories)
            {
                if (TypeExtensions.MatchesTypeName(typeName, registration.Key))
                {
                    adapter = registration.Value(system);
                    return true;
                }
            }

            adapter = null;
            return false;
        }

        public bool TryGetEventAdapterBindingType(string? typeName, [NotNullWhen(true)] out Type? type)
        {
            foreach (var bound in _setup.BindingTypes)
            {
                if (TypeExtensions.MatchesTypeName(typeName, bound))
                {
                    type = bound;
                    return true;
                }
            }

            // a registered adapter type is also a legal binding key
            foreach (var registered in _setup.AdapterFactories.Keys)
            {
                if (TypeExtensions.MatchesTypeName(typeName, registered))
                {
                    type = registered;
                    return true;
                }
            }

            type = null;
            return false;
        }

        public bool TryCreateStashOverflowConfigurator(string? typeName, [NotNullWhen(true)] out IStashOverflowStrategyConfigurator? configurator)
        {
            foreach (var registration in _setup.StashConfiguratorFactories)
            {
                if (TypeExtensions.MatchesTypeName(typeName, registration.Key))
                {
                    configurator = registration.Value();
                    return true;
                }
            }

            configurator = null;
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
