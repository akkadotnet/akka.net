//-----------------------------------------------------------------------
// <copyright file="Tagged.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public readonly object Payload;
        public readonly IImmutableSet<string> Tags;

        public Tagged(object payload, IEnumerable<string> tags)
        {
            Payload = payload;
            Tags = tags.ToImmutableHashSet();
        }

        public Tagged(object payload, IImmutableSet<string> tags)
        {
            Payload = payload;
            Tags = tags;
        }
    }
}