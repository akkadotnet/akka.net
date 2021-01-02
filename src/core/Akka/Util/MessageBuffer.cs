//-----------------------------------------------------------------------
// <copyright file="MessageBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// A non thread safe mutable message buffer that can be used to buffer messages inside actors.
    /// </summary>
    internal sealed class MessageBuffer : IEnumerable<(object Message, IActorRef Ref)>, IReadOnlyCollection<(object Message, IActorRef Ref)>
    {
        /// <summary>
        /// Create an empty message buffer.
        /// </summary>
        /// <returns>an empty message buffer</returns>
        public static MessageBuffer Empty() => new MessageBuffer();

        private readonly LinkedList<(object Message, IActorRef Ref)> buffer = new LinkedList<(object Message, IActorRef Ref)>();

        private MessageBuffer()
        {
        }

        /// <summary>
        /// Check if the message buffer is empty.
        /// </summary>
        public bool IsEmpty => buffer.First == null;

        /// <summary>
        /// Check if the message buffer is not empty.
        /// </summary>
        public bool NonEmpty => !IsEmpty;

        /// <summary>
        /// How many elements are in the message buffer.
        /// </summary>
        public int Count => buffer.Count;

        /// <summary>
        /// Add one element to the end of the message buffer.
        /// </summary>
        /// <param name="message">the message to buffer</param>
        /// <param name="ref">the actor to buffer</param>
        /// <returns>this message buffer</returns>
        public MessageBuffer Append(object message, IActorRef @ref)
        {
            buffer.AddLast((message, @ref));
            return this;
        }

        /// <summary>
        /// Remove the first element of the message buffer.
        /// </summary>
        public void DropHead()
        {
            if (NonEmpty)
                buffer.RemoveFirst();
        }

        /// <summary>
        /// Return the first element or an element containing null if the buffer is empty
        /// </summary>
        /// <returns></returns>
        public (object, IActorRef) Head => buffer.First?.Value ?? default;

        /// <summary>
        /// Returns an enumerator that iterates through the buffer
        /// </summary>
        /// <returns></returns>
        public IEnumerator<(object Message, IActorRef Ref)> GetEnumerator()
        {
            return buffer.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return buffer.GetEnumerator();
        }
    }

    /// <summary>
    /// A non thread safe mutable message buffer map that can be used to buffer messages inside actors.
    /// </summary>
    /// <typeparam name="TId">Id type</typeparam>
    internal sealed class MessageBufferMap<TId> : IEnumerable<KeyValuePair<TId, MessageBuffer>>, IReadOnlyCollection<KeyValuePair<TId, MessageBuffer>>
    {
        private readonly Dictionary<TId, MessageBuffer> bufferMap = new Dictionary<TId, MessageBuffer>();

        /// <summary>
        /// Check if the buffer map is empty.
        /// </summary>
        public bool IsEmpty => bufferMap.Count == 0;

        /// <summary>
        /// Check if the buffer map is not empty.
        /// </summary>
        public bool NonEmpty => !IsEmpty;

        /// <summary>
        /// the number of ids in the buffer map
        /// </summary>
        public int Count => bufferMap.Count;

        /// <summary>
        /// The number of elements in the buffers in the buffer map
        /// </summary>
        public int TotalCount => bufferMap.Values.Sum(i => i.Count);

        private MessageBuffer GetOrAddBuffer(TId id)
        {
            if (!bufferMap.TryGetValue(id, out var buffer))
            {
                buffer = MessageBuffer.Empty();
                bufferMap[id] = buffer;
            }
            return buffer;
        }

        /// <summary>
        /// Add an id to the buffer map
        /// </summary>
        /// <param name="id">id to add</param>
        public void Add(TId id)
        {
            GetOrAddBuffer(id);
        }

        /// <summary>
        /// Append an element to the buffer for an id.
        /// </summary>
        /// <param name="id">the id to add the element to</param>
        /// <param name="message">the message to buffer</param>
        /// <param name="ref">the actor to buffer</param>
        public void Append(TId id, object message, IActorRef @ref)
        {
            var buffer = GetOrAddBuffer(id);
            buffer.Append(message, @ref);
        }

        /// <summary>
        /// Remove the buffer for an id.
        /// </summary>
        /// <param name="id">the id to remove the buffer for</param>
        public void Remove(TId id)
        {
            bufferMap.Remove(id);
        }

        /// <summary>
        /// Remove the buffer for an id, but publish a <see cref="Dropped"/> for each dropped buffered message
        /// </summary>
        /// <param name="id"></param>
        /// <param name="reason"></param>
        /// <param name="deadLetters"></param>
        /// <returns>how many buffered messages were dropped</returns>
        public int Drop(TId id, string reason, IActorRef deadLetters)
        {
            if (bufferMap.TryGetValue(id, out var buffer))
            {
                if (buffer.NonEmpty)
                {
                    foreach (var (msg, @ref) in buffer)
                        deadLetters.Tell(new Dropped(msg, reason, @ref, ActorRefs.NoSender));
                }
            }
            Remove(id);
            return buffer?.Count ?? 0;
        }

        /// <summary>
        /// Check if the buffer map contains an id.
        /// </summary>
        /// <param name="id">the id to check for</param>
        /// <returns>true if the buffer contains the given id</returns>
        public bool Contains(TId id) => bufferMap.ContainsKey(id);

        /// <summary>
        /// Get the message buffer for an id, or an empty buffer if the id doesn't exist in the map.
        /// </summary>
        /// <param name="id">the id to get the message buffer for</param>
        /// <returns>the message buffer for the given id or an empty buffer if the id doesn't exist</returns>
        public MessageBuffer GetOrEmpty(TId id)
        {
            if (bufferMap.TryGetValue(id, out var buffer))
                return buffer;
            return MessageBuffer.Empty();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the buffer map
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<TId, MessageBuffer>> GetEnumerator()
        {
            return bufferMap.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return bufferMap.GetEnumerator();
        }
    }
}
