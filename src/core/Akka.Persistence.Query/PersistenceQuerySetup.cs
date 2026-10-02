//-----------------------------------------------------------------------
// <copyright file="PersistenceQuerySetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Registers read journal provider types named by a read journal plugin's <c>class</c> setting,
    /// so PersistenceQuery can create them without reflection (required when Akka.DynamicTypeLoading is off).
    /// Like any <see cref="Setup"/>, a second instance passed to <see cref="ActorSystemSetup.And{T}"/>
    /// replaces the first; use <see cref="Merge"/> to combine registrations.
    /// </summary>
    public sealed class PersistenceQuerySetup : Setup
    {
        /// <summary>
        /// A setup with no registrations.
        /// </summary>
        public static PersistenceQuerySetup Empty { get; } =
            new(ImmutableDictionary<Type, Func<ExtendedActorSystem, Config, IReadJournalProvider>>.Empty);

        private PersistenceQuerySetup(ImmutableDictionary<Type, Func<ExtendedActorSystem, Config, IReadJournalProvider>> providers)
        {
            ProviderFactories = providers;
        }

        internal ImmutableDictionary<Type, Func<ExtendedActorSystem, Config, IReadJournalProvider>> ProviderFactories { get; }

        /// <summary>
        /// Registers a read journal provider type named by a read journal plugin's <c>class</c> setting.
        /// </summary>
        /// <typeparam name="TProvider">The provider type.</typeparam>
        /// <param name="factory">Called with the actor system and the plugin's config section.</param>
        /// <returns>A new setup that also holds this registration.</returns>
        public PersistenceQuerySetup WithReadJournal<TProvider>(Func<ExtendedActorSystem, Config, TProvider> factory)
            where TProvider : class, IReadJournalProvider
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            return new PersistenceQuerySetup(ProviderFactories.SetItem(typeof(TProvider), (system, config) => factory(system, config)));
        }

        /// <summary>
        /// Returns a setup with both sets of registrations; on the same type, <paramref name="other"/> wins.
        /// </summary>
        /// <param name="other">The setup to merge on top of this one.</param>
        /// <returns>The combined setup.</returns>
        public PersistenceQuerySetup Merge(PersistenceQuerySetup other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            return new PersistenceQuerySetup(ProviderFactories.SetItems(other.ProviderFactories));
        }

        /// <summary>
        /// Every registered type, for diagnostics.
        /// </summary>
        public IReadOnlyCollection<Type> RegisteredTypes => ProviderFactories.Keys.ToImmutableArray();
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// The one place PersistenceQuery reads code-based registrations from; the counterpart of
    /// <c>PersistencePluginRegistry</c> in Akka.Persistence. Answers the Setup arm only: the site writes the
    /// guard and reflection arms out itself, see <see cref="AkkaFeatures"/>.
    /// </summary>
    internal sealed class PersistenceQueryRegistry
    {
        private readonly PersistenceQuerySetup _setup;

        public PersistenceQueryRegistry(PersistenceQuerySetup? setup)
        {
            _setup = setup ?? PersistenceQuerySetup.Empty;
        }

        public static PersistenceQueryRegistry From(ActorSystemSetup setup)
            => new(setup.Get<PersistenceQuerySetup>().GetOrElse(null!));

        public bool TryCreateProvider(string? typeName, ExtendedActorSystem system, Config pluginConfig, [NotNullWhen(true)] out IReadJournalProvider? provider)
        {
            foreach (var registration in _setup.ProviderFactories)
            {
                if (TypeExtensions.MatchesTypeName(typeName, registration.Key))
                {
                    provider = registration.Value(system, pluginConfig);
                    return true;
                }
            }

            provider = null;
            return false;
        }
    }
}
