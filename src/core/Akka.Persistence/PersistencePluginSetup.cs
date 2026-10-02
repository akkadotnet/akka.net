//-----------------------------------------------------------------------
// <copyright file="PersistencePluginSetup.cs" company="Akka.NET Project">
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
using Akka.Persistence.Journal;

namespace Akka.Persistence
{
    /// <summary>
    /// Registers persistence plugin types in code so Akka.Persistence can build the journals, snapshot
    /// stores, event adapters and stash-overflow configurators that HOCON names without reflection.
    /// Required when the Akka.DynamicTypeLoading feature switch is off (Native AOT, trimmed apps);
    /// optional otherwise. HOCON stays the source of truth: a registration is used when its type is the
    /// type a HOCON setting names (by full name and, if given, assembly name).
    /// Like any <see cref="Setup"/>, a second instance passed to <see cref="ActorSystemSetup.And{T}"/>
    /// replaces the first; use <see cref="Merge"/> to combine registrations.
    /// </summary>
    public sealed class PersistencePluginSetup : Setup
    {
        /// <summary>
        /// A setup with no registrations.
        /// </summary>
        public static PersistencePluginSetup Empty { get; } = new(
            ImmutableDictionary<Type, Func<Config, Props>>.Empty,
            ImmutableDictionary<Type, Func<ExtendedActorSystem, IEventAdapter>>.Empty,
            ImmutableHashSet<Type>.Empty,
            ImmutableDictionary<Type, Func<IStashOverflowStrategyConfigurator>>.Empty);

        private PersistencePluginSetup(
            ImmutableDictionary<Type, Func<Config, Props>> plugins,
            ImmutableDictionary<Type, Func<ExtendedActorSystem, IEventAdapter>> eventAdapters,
            ImmutableHashSet<Type> eventAdapterBindings,
            ImmutableDictionary<Type, Func<IStashOverflowStrategyConfigurator>> stashOverflowStrategies)
        {
            PluginFactories = plugins;
            AdapterFactories = eventAdapters;
            BindingTypes = eventAdapterBindings;
            StashConfiguratorFactories = stashOverflowStrategies;
        }

        // journals and snapshot stores: both are an actor named by a plugin section's `class` setting
        internal ImmutableDictionary<Type, Func<Config, Props>> PluginFactories { get; }

        // already wrapped so that the lookup site always gets an IEventAdapter
        internal ImmutableDictionary<Type, Func<ExtendedActorSystem, IEventAdapter>> AdapterFactories { get; }

        internal ImmutableHashSet<Type> BindingTypes { get; }

        internal ImmutableDictionary<Type, Func<IStashOverflowStrategyConfigurator>> StashConfiguratorFactories { get; }

        /// <summary>
        /// Registers a journal type named by a plugin section's <c>class</c> setting.
        /// </summary>
        /// <typeparam name="TJournal">The journal actor type.</typeparam>
        /// <param name="factory">Called inside the actor's creation context with the plugin's config section (fallbacks applied).</param>
        /// <returns>A new setup that also holds this registration.</returns>
        public PersistencePluginSetup WithJournal<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TJournal>(
            Func<Config, TJournal> factory) where TJournal : ActorBase
            => WithPlugin(factory);

        /// <summary>
        /// Registers a snapshot store type named by a plugin section's <c>class</c> setting.
        /// </summary>
        /// <typeparam name="TSnapshotStore">The snapshot store actor type.</typeparam>
        /// <param name="factory">Called inside the actor's creation context with the plugin's config section (fallbacks applied).</param>
        /// <returns>A new setup that also holds this registration.</returns>
        public PersistencePluginSetup WithSnapshotStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TSnapshotStore>(
            Func<Config, TSnapshotStore> factory) where TSnapshotStore : ActorBase
            => WithPlugin(factory);

        private PersistencePluginSetup WithPlugin<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TActor>(
            Func<Config, TActor> factory) where TActor : ActorBase
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            // the closed generic producer is created here, where the type is known, so the trimmer keeps what Props needs
            return new PersistencePluginSetup(
                PluginFactories.SetItem(typeof(TActor), config => Props.CreateBy(new PluginActorProducer<TActor>(factory, config))),
                AdapterFactories, BindingTypes, StashConfiguratorFactories);
        }

        /// <summary>
        /// Registers an event adapter type named under a journal's <c>event-adapters</c>.
        /// </summary>
        /// <typeparam name="TAdapter">The adapter type.</typeparam>
        /// <param name="factory">Creates the adapter. Called once per journal section that names the adapter.</param>
        /// <returns>A new setup that also holds this registration.</returns>
        /// <exception cref="ArgumentException"><typeparamref name="TAdapter"/> implements none of IEventAdapter, IWriteEventAdapter, IReadEventAdapter.</exception>
        public PersistencePluginSetup WithEventAdapter<TAdapter>(Func<ExtendedActorSystem, TAdapter> factory) where TAdapter : class
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            // same precedence and wrapping as the reflection path in EventAdapters
            Func<ExtendedActorSystem, IEventAdapter> wrapped;
            if (typeof(IEventAdapter).IsAssignableFrom(typeof(TAdapter)))
                wrapped = system => (IEventAdapter)factory(system);
            else if (typeof(IWriteEventAdapter).IsAssignableFrom(typeof(TAdapter)))
                wrapped = system => new NoopReadEventAdapter((IWriteEventAdapter)factory(system));
            else if (typeof(IReadEventAdapter).IsAssignableFrom(typeof(TAdapter)))
                wrapped = system => new NoopWriteEventAdapter((IReadEventAdapter)factory(system));
            else
                throw new ArgumentException(
                    $"[{typeof(TAdapter)}] does not implement any event adapter interface " +
                    $"({nameof(IEventAdapter)}, {nameof(IWriteEventAdapter)} or {nameof(IReadEventAdapter)}).");

            return new PersistencePluginSetup(
                PluginFactories, AdapterFactories.SetItem(typeof(TAdapter), wrapped), BindingTypes, StashConfiguratorFactories);
        }

        /// <summary>
        /// Registers a type named as a key under a journal's <c>event-adapter-bindings</c>.
        /// </summary>
        /// <typeparam name="TEvent">The bound event type.</typeparam>
        /// <returns>A new setup that also holds this registration.</returns>
        public PersistencePluginSetup WithEventAdapterBinding<TEvent>()
            => WithEventAdapterBinding(typeof(TEvent));

        /// <inheritdoc cref="WithEventAdapterBinding{TEvent}"/>
        /// <param name="eventType">The bound event type.</param>
        public PersistencePluginSetup WithEventAdapterBinding(Type eventType)
        {
            if (eventType is null)
                throw new ArgumentNullException(nameof(eventType));

            return new PersistencePluginSetup(
                PluginFactories, AdapterFactories, BindingTypes.Add(eventType), StashConfiguratorFactories);
        }

        /// <summary>
        /// Registers a configurator named by <c>akka.persistence.internal-stash-overflow-strategy</c>.
        /// </summary>
        /// <typeparam name="TConfigurator">The configurator type.</typeparam>
        /// <param name="factory">Creates the configurator.</param>
        /// <returns>A new setup that also holds this registration.</returns>
        public PersistencePluginSetup WithStashOverflowStrategy<TConfigurator>(Func<TConfigurator> factory)
            where TConfigurator : IStashOverflowStrategyConfigurator
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            return new PersistencePluginSetup(
                PluginFactories, AdapterFactories, BindingTypes,
                StashConfiguratorFactories.SetItem(typeof(TConfigurator), () => factory()));
        }

        /// <summary>
        /// Returns a setup with both sets of registrations; on the same type, <paramref name="other"/> wins.
        /// </summary>
        /// <param name="other">The setup to merge on top of this one.</param>
        /// <returns>The combined setup.</returns>
        public PersistencePluginSetup Merge(PersistencePluginSetup other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            return new PersistencePluginSetup(
                PluginFactories.SetItems(other.PluginFactories),
                AdapterFactories.SetItems(other.AdapterFactories),
                BindingTypes.Union(other.BindingTypes),
                StashConfiguratorFactories.SetItems(other.StashConfiguratorFactories));
        }

        /// <summary>
        /// Every registered type, for diagnostics.
        /// </summary>
        public IReadOnlyCollection<Type> RegisteredTypes
            => PluginFactories.Keys
                .Concat(AdapterFactories.Keys)
                .Concat(BindingTypes)
                .Concat(StashConfiguratorFactories.Keys)
                .Distinct()
                .ToImmutableArray();
    }
}
