//-----------------------------------------------------------------------
// <copyright file="ExposedPublisherReceive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Adapts actor receive handling while buffering messages until an exposed publisher is available.
    /// </summary>
    public abstract class ExposedPublisherReceive
    {
        /// <summary>
        /// The receive handler applied to messages buffered before the publisher was exposed.
        /// </summary>
        public readonly Receive ActiveReceive;
        /// <summary>
        /// Handles buffered messages not consumed by <see cref="ActiveReceive"/>.
        /// </summary>
        public readonly Action<object> Unhandled;

        private readonly LinkedList<object> _stash = new();

        /// <summary>
        /// Creates the receive adapter used before and after publisher exposure.
        /// </summary>
        /// <param name="activeReceive">The receive handler used to process buffered messages after exposure.</param>
        /// <param name="unhandled">The action called for buffered messages not handled by <paramref name="activeReceive"/>.</param>
        protected ExposedPublisherReceive(Receive activeReceive, Action<object> unhandled)
        {
            ActiveReceive = activeReceive;
            Unhandled = unhandled;
        }

        /// <summary>
        /// Processes the exposed publisher and enables replay of buffered messages.
        /// </summary>
        /// <param name="publisher">The publisher exposed to the actor.</param>
        internal abstract void ReceiveExposedPublisher(ExposedPublisher publisher);

        /// <summary>
        /// Exposes a publisher or buffers a message until a publisher is exposed.
        /// </summary>
        /// <param name="message">The publisher exposure message or a message to buffer.</param>
        /// <returns><see langword="true"/> for every message passed to this adapter.</returns>
        public bool Apply(object message)
        {
            ExposedPublisher publisher;
            if ((publisher = message as ExposedPublisher) != null)
            {
                ReceiveExposedPublisher(publisher);
                if (_stash.Any())
                {
                    // we don't use sender() so this is alright
                    foreach (var msg in _stash)
                        if (!ActiveReceive(msg)) Unhandled(msg);
                }
            }
            else
                _stash.AddLast(message);

            return true;
        }
    }
}
