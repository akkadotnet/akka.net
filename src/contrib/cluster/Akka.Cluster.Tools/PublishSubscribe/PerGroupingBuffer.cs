//-----------------------------------------------------------------------
// <copyright file="PerGroupingBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using BufferedMessages = System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object, Akka.Actor.IActorRef>>;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class PerGroupingBuffer
    {
        private readonly Dictionary<string, BufferedMessages> _buffers = new Dictionary<string, BufferedMessages>();
        private int _totalBufferSize = 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="grouping">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="originalSender">TBD</param>
        /// <param name="action">TBD</param>
        public void BufferOr(string grouping, object message, IActorRef originalSender, Action action)
        {
            if (_buffers.TryGetValue(grouping, out var messages))
            {
                _buffers[grouping].Add(new KeyValuePair<object, IActorRef>(message, originalSender));
                _totalBufferSize += 1;
            }
            
            action();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="grouping">TBD</param>
        /// <param name="recipient">TBD</param>
        public void RecreateAndForwardMessagesIfNeeded(string grouping, Func<IActorRef> recipient)
        {
            if (_buffers.TryGetValue(grouping, out var messages) && messages.Count > 0)
            {
                ForwardMessages(messages, recipient());
                _totalBufferSize -= messages.Count;
            }
            _buffers.Remove(grouping);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="grouping">TBD</param>
        /// <param name="recipient">TBD</param>
        public void ForwardMessages(string grouping, IActorRef recipient)
        {
            if (_buffers.TryGetValue(grouping, out var messages))
            {
                ForwardMessages(messages, recipient);
                _totalBufferSize -= messages.Count;
            }
            _buffers.Remove(grouping);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="grouping">TBD</param>
        public void InitializeGrouping(string grouping)
        {
            _buffers.Add(grouping, new BufferedMessages());
        }

        private void ForwardMessages(BufferedMessages messages, IActorRef recipient)
        {
            messages.ForEach(c =>
            {
                recipient.Tell(c.Key, c.Value);
            });
        }
    }
}
