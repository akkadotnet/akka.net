//-----------------------------------------------------------------------
// <copyright file="EventAdapterDetails.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// An event adapter registered in code, together with the event types it is bound to. It goes into
    /// <see cref="JournalDetails.Create{TJournal}"/> and replaces the HOCON <c>event-adapters</c> entry and
    /// its <c>event-adapter-bindings</c> lines.
    /// </summary>
    public sealed class EventAdapterDetails
    {
        private readonly Func<ExtendedActorSystem, IEventAdapter> _factory;

        private EventAdapterDetails(string name, Func<ExtendedActorSystem, IEventAdapter> factory, ImmutableArray<Type> boundTypes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An event adapter name is required.", nameof(name));

            Name = name;
            _factory = factory;
            BoundTypes = boundTypes;
        }

        /// <summary>
        /// The adapter's name, unique within its journal. A HOCON <c>event-adapters</c> entry of the same name
        /// loses to this one.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The event types this adapter is bound to. A type also binds its subtypes, as in HOCON.
        /// </summary>
        public IReadOnlyList<Type> BoundTypes { get; }

        /// <summary>
        /// Registers an adapter that reads and writes.
        /// </summary>
        /// <param name="name">The adapter's name.</param>
        /// <param name="factory">Creates the adapter; called once per journal.</param>
        /// <param name="boundTypes">The event types the adapter is bound to.</param>
        /// <returns>The record.</returns>
        public static EventAdapterDetails Create(string name, Func<ExtendedActorSystem, IEventAdapter> factory, params Type[] boundTypes)
            => new(name, NotNull(factory), Bound(boundTypes));

        /// <summary>
        /// Registers an adapter that only writes. Reads pass events through unchanged.
        /// </summary>
        /// <param name="name">The adapter's name.</param>
        /// <param name="factory">Creates the adapter; called once per journal.</param>
        /// <param name="boundTypes">The event types the adapter is bound to.</param>
        /// <returns>The record.</returns>
        public static EventAdapterDetails Create(string name, Func<ExtendedActorSystem, IWriteEventAdapter> factory, params Type[] boundTypes)
        {
            NotNull(factory);
            return new(name, system => new NoopReadEventAdapter(factory(system)), Bound(boundTypes));
        }

        /// <summary>
        /// Registers an adapter that only reads. Writes pass events through unchanged.
        /// </summary>
        /// <param name="name">The adapter's name.</param>
        /// <param name="factory">Creates the adapter; called once per journal.</param>
        /// <param name="boundTypes">The event types the adapter is bound to.</param>
        /// <returns>The record.</returns>
        public static EventAdapterDetails Create(string name, Func<ExtendedActorSystem, IReadEventAdapter> factory, params Type[] boundTypes)
        {
            NotNull(factory);
            return new(name, system => new NoopWriteEventAdapter(factory(system)), Bound(boundTypes));
        }

        internal IEventAdapter CreateAdapter(ExtendedActorSystem system) => _factory(system);

        private static T NotNull<T>(T factory) where T : class
            => factory ?? throw new ArgumentNullException(nameof(factory));

        private static ImmutableArray<Type> Bound(Type[] boundTypes)
        {
            if (boundTypes is null)
                throw new ArgumentNullException(nameof(boundTypes));
            foreach (var type in boundTypes)
            {
                if (type is null)
                    throw new ArgumentException("A bound type is null.", nameof(boundTypes));
            }

            return boundTypes.ToImmutableArray();
        }
    }
}
