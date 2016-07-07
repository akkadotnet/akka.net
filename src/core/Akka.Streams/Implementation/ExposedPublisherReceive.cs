//-----------------------------------------------------------------------
// <copyright file="ExposedPublisherReceive.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    public abstract class ExposedPublisherReceive
    {
        public readonly Receive ActiveReceive;
        public readonly Action<object> Unhandled;

        private readonly LinkedList<object> _stash = new LinkedList<object>();

        protected ExposedPublisherReceive(Receive activeReceive, Action<object> unhandled)
        {
            ActiveReceive = activeReceive;
            Unhandled = unhandled;
        }

        internal abstract void ReceiveExposedPublisher(ExposedPublisher publisher);

        public bool Apply(object message)
        {
            ExposedPublisher publisher;
            if ((publisher = message as ExposedPublisher) != null)
            {
                ReceiveExposedPublisher(publisher);
                if (_stash.Any())
                {
                    // we don't use sender() so this is allright
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