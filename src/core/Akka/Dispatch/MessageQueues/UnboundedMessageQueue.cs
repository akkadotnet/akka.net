//-----------------------------------------------------------------------
// <copyright file="UnboundedMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using TQueue = System.Collections.Concurrent.ConcurrentQueue<Akka.Actor.Envelope>;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Similar to SysMsg queue.
    /// </summary>
    internal class LockFreeQueue<T>
    {
        internal sealed class Node
        {
            public T Value;
            public Node Next;

            public Node Unlink()
            {
                Next = null;
                Value = default;
                return this;
            }
        }

        public Node Head;
        public Node Tail;

        public bool IsEmpty => Head.Next == null;

        public int Count => SizeInner(Head.Next, 0);

        internal static int SizeInner(Node head, int acc)
        {
            while (true)
            {
                if (head == null) return acc;
                head = head.Next;
                acc += 1;
            }
        }

        public LockFreeQueue()
        {
            Head = Tail = new Node();
        }

        public bool TryDequeue(out T value)
        {
            if (Head.Next != null)
            {
                Head = Head.Next;
                value = Head.Value;
                return true;
            }

            value = default;
            return false;
        }

        public void Enqueue(T value)
        {
            var node = new Node();
            node.Value = value;

            var oldTail = Interlocked.Exchange(ref Tail, node);
            oldTail.Next = node;
        }
    }

    /// <summary> An unbounded mailbox message queue. </summary>
    public class UnboundedMessageQueue : IMessageQueue, IUnboundedMessageQueueSemantics
    {
        private readonly LockFreeQueue<Envelope> _queue = new LockFreeQueue<Envelope>();

        /// <inheritdoc cref="IMessageQueue"/>
        public bool HasMessages
        {
            get { return !_queue.IsEmpty; }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public int Count
        {
            get { return _queue.Count; }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            _queue.Enqueue(envelope);
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryDequeue(out envelope);
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            Envelope msg;
            while (TryDequeue(out msg))
            {
                deadletters.Enqueue(owner, msg);
            }
        }
    }
}
