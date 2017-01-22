//-----------------------------------------------------------------------
// <copyright file="PerGroupingBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using BufferedMessages = System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object, Akka.Actor.IActorRef>>;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    internal class PerGroupingBuffer
    {
        private readonly Dictionary<string, BufferedMessages> _buffers = new Dictionary<string, BufferedMessages>();
        private int _totalBufferSize = 0;

        public void BufferOr(string grouping, object message, IActorRef originalSender, Action action)
        {
            BufferedMessages messages = null;
            if (_buffers.TryGetValue(grouping, out messages))
            {
                _buffers[grouping].Add(new KeyValuePair<object, IActorRef>(message, originalSender));
                _totalBufferSize += 1;
            }
			
            action();
        }

        public void RecreateAndForwardMessagesIfNeeded(string grouping, Func<IActorRef> recipient)
        {
            BufferedMessages messages;
            if (_buffers.TryGetValue(grouping, out messages) && messages.Count > 0)
            {
                ForwardMessages(messages, recipient());
                _totalBufferSize -= messages.Count;
            }
            _buffers.Remove(grouping);
        }

        public void ForwardMessages(string grouping, IActorRef recipient)
        {
            BufferedMessages messages;
            if (_buffers.TryGetValue(grouping, out messages))
            {
                ForwardMessages(messages, recipient);
                _totalBufferSize -= messages.Count;
            }
            _buffers.Remove(grouping);
        }

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