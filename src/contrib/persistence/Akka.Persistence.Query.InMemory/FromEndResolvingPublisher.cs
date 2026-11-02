//-----------------------------------------------------------------------
// <copyright file="FromEndResolvingPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.InMemory
{
    /// <summary>
    /// Base for the InMemory query publishers that supports resolving a <see cref="FromEnd"/> ("last N events")
    /// offset into a concrete forward start offset. When the <see cref="ReplayStart"/> is a from-end start, the
    /// publisher asks the journal how many events currently match the query (via
    /// <see cref="MemoryJournal.SelectEventCount"/>) and then begins its normal forward replay at
    /// <c>max(0, count - N)</c>. Both the by-tag and all-events publishers share this handshake so the resolution
    /// logic lives in a single place.
    /// </summary>
    internal abstract class FromEndResolvingPublisher : ActorPublisher<EventEnvelope>
    {
        private readonly ReplayStart _start;

        protected FromEndResolvingPublisher(ReplayStart start, string writeJournalPluginId)
        {
            _start = start;
            CurrentOffset = start.Offset;
            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected int CurrentOffset;
        protected IActorRef JournalRef { get; }

        /// <summary>
        /// True when this query must resolve a <see cref="FromEnd"/> start position before replaying.
        /// </summary>
        protected bool IsFromEnd => _start.IsFromEnd;

        /// <summary>
        /// The tag whose events are counted to resolve the from-end position, or <c>null</c> for all events.
        /// </summary>
        protected abstract string FromEndTag { get; }

        protected abstract void ReceiveInitialRequest();

        /// <summary>
        /// Asks the journal for the current matching event count, then resumes the normal initial replay once the
        /// concrete start offset is known. Call this from the initial request handler when <see cref="IsFromEnd"/>.
        /// </summary>
        protected void ResolveFromEnd()
        {
            JournalRef.Tell(new MemoryJournal.SelectEventCount(FromEndTag, Self));
            Context.Become(ResolvingFromEnd);
        }

        private bool ResolvingFromEnd(object message)
        {
            switch (message)
            {
                case MemoryJournal.EventCount count:
                    CurrentOffset = Math.Max(0, count.Count - _start.FromEndCount);
                    ReceiveInitialRequest();
                    return true;
                case MemoryJournal.EventCountFailure failure:
                    // surface a failed count query instead of hanging the stream forever
                    OnErrorThenStop(failure.Cause);
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    // Outstanding demand is tracked by ActorPublisher; periodic Continue ticks and any other
                    // transient message are ignored until the count reply arrives.
                    return true;
            }
        }
    }
}
