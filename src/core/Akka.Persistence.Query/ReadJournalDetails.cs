//-----------------------------------------------------------------------
// <copyright file="ReadJournalDetails.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// A read journal registered in code. <see cref="PersistenceQuery"/> uses it for the plugin at
    /// <see cref="PersistencePluginDetails.PluginId"/> without a HOCON <c>class</c> setting and without reflection.
    /// Add one to a <see cref="PersistenceSetup"/> with <see cref="PersistenceSetupExtensions.WithReadJournal{TProvider}"/>.
    /// </summary>
    internal sealed class ReadJournalDetails : PersistencePluginDetails
    {
        private readonly Func<ExtendedActorSystem, Config, IReadJournalProvider> _createProvider;

        private ReadJournalDetails(string pluginId, Func<ExtendedActorSystem, Config, IReadJournalProvider> createProvider, Config? defaultConfig)
            : base(pluginId, defaultConfig)
        {
            _createProvider = createProvider;
        }

        /// <summary>
        /// Registers a read journal provider for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TProvider">The provider type.</typeparam>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.query.journal.my-journal</c>.</param>
        /// <param name="factory">Called with the actor system and the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <returns>The record.</returns>
        public static ReadJournalDetails Create<TProvider>(
            string pluginId,
            Func<ExtendedActorSystem, Config, TProvider> factory,
            Config? defaultConfig = null) where TProvider : class, IReadJournalProvider
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            return new ReadJournalDetails(pluginId, (system, config) => factory(system, config), defaultConfig);
        }

        internal IReadJournalProvider CreateProvider(ExtendedActorSystem system, Config config) => _createProvider(system, config);
    }

    /// <summary>
    /// Adds read journals to a <see cref="PersistenceSetup"/>.
    /// </summary>
    internal static class PersistenceSetupExtensions
    {
        /// <summary>
        /// Registers a read journal provider for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TProvider">The provider type.</typeparam>
        /// <param name="setup">The setup to add to.</param>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.query.journal.my-journal</c>.</param>
        /// <param name="factory">Called with the actor system and the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <returns>A new setup that also holds this read journal.</returns>
        public static PersistenceSetup WithReadJournal<TProvider>(
            this PersistenceSetup setup,
            string pluginId,
            Func<ExtendedActorSystem, Config, TProvider> factory,
            Config? defaultConfig = null) where TProvider : class, IReadJournalProvider
        {
            if (setup is null)
                throw new ArgumentNullException(nameof(setup));

            return setup.WithPlugin(ReadJournalDetails.Create(pluginId, factory, defaultConfig));
        }
    }
}
