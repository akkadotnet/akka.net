// -----------------------------------------------------------------------
//  <copyright file="PersistenceSetupHostingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Hosting;

namespace Akka.Persistence.Hosting
{
    public static class PersistenceSetupHostingExtensions
    {
        /// <summary>
        /// Registers persistence plugins in code through a <see cref="PersistenceSetup"/>, which is what a
        /// trimmed or Native AOT app needs to start journals, snapshot stores and read journals without
        /// reflection. Every call builds on the setup the earlier calls left, so plugin packages can each add
        /// their own.
        /// </summary>
        /// <example>
        /// <code>
        /// builder.WithPersistenceSetup(setup => setup
        ///     .WithJournal("akka.persistence.journal.my-journal", config => new MyJournal(config))
        ///     .WithSnapshotStore("akka.persistence.snapshot-store.my-store", config => new MyStore(config)));
        /// </code>
        /// </example>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="configure">Adds to the setup. It gets the setup built so far, or an empty one.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithPersistenceSetup(
            this AkkaConfigurationBuilder builder,
            Func<PersistenceSetup, PersistenceSetup> configure)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));
            if (configure is null)
                throw new ArgumentNullException(nameof(configure));

            var existing = builder.Setups.OfType<PersistenceSetup>().FirstOrDefault();
            var updated = configure(existing ?? PersistenceSetup.Create());

            builder.AddSetup(updated);

            // an ActorSystemSetup keeps one PersistenceSetup, so the old one must go once the new one is in.
            // AddSetup does nothing after the builder has started, and then the old one stays.
            if (existing is not null && !ReferenceEquals(existing, updated) && builder.Setups.Contains(updated))
                builder.Setups.Remove(existing);

            return builder;
        }
    }
}
