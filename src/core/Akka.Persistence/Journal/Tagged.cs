//-----------------------------------------------------------------------
// <copyright file="Tagged.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// The journal may support tagging of events that are used by the
    /// `EventsByTag` query and it may support specifying the tags via an
    /// <see cref="IEventAdapter"/> that wraps the events
    /// in a <see cref="Tagged"/> with the given <see cref="Tags"/>. The journal may support other
    /// ways of doing tagging. Please consult the documentation of the specific
    /// journal implementation for more information.
    /// The journal will unwrap the event and store the <see cref="Payload"/>.
    /// </summary>
    public struct Tagged
    {
        /// <summary>
        /// Creates a tagged payload from a sequence of tags.
        /// </summary>
        /// <param name="payload">Event payload to store in the journal.</param>
        /// <param name="tags">Tags associated with the event.</param>
        public Tagged(object payload, IEnumerable<string> tags)
        {
            Payload = payload;
            Tags = tags.ToImmutableHashSet();
        }

        /// <summary>
        /// Creates a tagged payload from an immutable set of tags.
        /// </summary>
        /// <param name="payload">Event payload to store in the journal.</param>
        /// <param name="tags">Immutable tags associated with the event.</param>
        public Tagged(object payload, IImmutableSet<string> tags)
        {
            Payload = payload;
            Tags = tags;
        }

        /// <summary>
        /// Event payload that the journal stores.
        /// </summary>
        public object Payload { get; }

        /// <summary>
        /// Immutable set of tags associated with the event.
        /// </summary>
        public IImmutableSet<string> Tags { get; }
    }
}
