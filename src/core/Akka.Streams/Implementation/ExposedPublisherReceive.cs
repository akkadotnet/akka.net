//-----------------------------------------------------------------------
// <copyright file="ExposedPublisherReceive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ExposedPublisherReceive
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Receive ActiveReceive;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Action<object> Unhandled;

        private readonly LinkedList<object> _stash = new LinkedList<object>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="activeReceive">TBD</param>
        /// <param name="unhandled">TBD</param>
        protected ExposedPublisherReceive(Receive activeReceive, Action<object> unhandled)
        {
            ActiveReceive = activeReceive;
            Unhandled = unhandled;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        internal abstract void ReceiveExposedPublisher(ExposedPublisher publisher);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
