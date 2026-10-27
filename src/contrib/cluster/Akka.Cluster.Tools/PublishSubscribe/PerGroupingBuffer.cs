//-----------------------------------------------------------------------
// <copyright file="PerGroupingBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using BufferedMessages = System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object, Akka.Actor.IActorRef>>;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// Buffers messages by grouping key during topic or group actor termination transitions.
    /// </summary>
    internal class PerGroupingBuffer
    {
        private readonly Dictionary<string, BufferedMessages> _buffers = new();
        private int _totalBufferSize = 0;

        /// <summary>
        /// Buffers the message when its group is being recreated; otherwise runs the action immediately.
        /// </summary>
        /// <param name="grouping">Key identifying the topic or group whose messages are being buffered.</param>
        /// <param name="message">Message to buffer for the group.</param>
        /// <param name="originalSender">Original sender to preserve when forwarding the buffered message.</param>
        /// <param name="action">Operation to invoke immediately when no buffer exists for the group.</param>
        public void BufferOr(string grouping, object message, IActorRef originalSender, Action action)
        {
            if (_buffers.TryGetValue(grouping, out var messages))
            {
                messages.Add(new KeyValuePair<object, IActorRef>(message, originalSender));
                _totalBufferSize += 1;
            }
            else
                action();
        }

        /// <summary>
        /// Recreates the recipient for a group with buffered messages, forwards them, and removes the buffer.
        /// </summary>
        /// <param name="grouping">Key identifying the group whose buffer should be drained.</param>
        /// <param name="recipient">Factory that creates the actor to receive the buffered messages.</param>
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
        /// Forwards buffered messages to the recipient and removes the group's buffer.
        /// </summary>
        /// <param name="grouping">Key identifying the group whose buffer should be drained.</param>
        /// <param name="recipient">Actor that receives the buffered messages.</param>
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
        /// Starts buffering messages under the specified grouping key.
        /// </summary>
        /// <param name="grouping">Key identifying the group whose messages should be buffered.</param>
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
