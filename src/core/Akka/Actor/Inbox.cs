//-----------------------------------------------------------------------
// <copyright file="Inbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Configuration;

namespace Akka.Actor
{
    /// <summary>
    /// Describes a pending request for one or more messages from an inbox actor.
    /// </summary>
    internal interface IQuery
    {
        /// <summary>
        /// The monotonic-clock deadline after which this query expires.
        /// </summary>
        TimeSpan Deadline { get; }
        /// <summary>
        /// The actor reference that will receive the query result.
        /// </summary>
        IActorRef Client { get; }
        /// <summary>
        /// Returns this query with a client reference for delivering its result.
        /// </summary>
        /// <param name="client">The actor that requested the query.</param>
        /// <returns>A query with the same deadline and selection criteria and the supplied client.</returns>
        IQuery WithClient(IActorRef client);
    }

    /// <summary>
    /// A request to receive the next message before a deadline.
    /// </summary>
    internal struct Get : IQuery
    {
        /// <summary>
        /// Creates a request to receive the next inbox message.
        /// </summary>
        /// <param name="deadline">The monotonic-clock deadline for the request.</param>
        /// <param name="client">The actor to receive the message, or <c>null</c> until the inbox actor records the sender.</param>
        public Get(TimeSpan deadline, IActorRef client = null)
            : this()
        {
            Deadline = deadline;
            Client = client;
        }

        /// <summary>
        /// The deadline by which a message must be received.
        /// </summary>
        public TimeSpan Deadline { get; private set; }
        /// <summary>
        /// The actor that will receive the selected message.
        /// </summary>
        public IActorRef Client { get; private set; }
        /// <summary>
        /// Copies this request with a different result recipient.
        /// </summary>
        /// <param name="client">The actor to receive the selected message.</param>
        /// <returns>A request with the same deadline and the supplied client.</returns>
        public IQuery WithClient(IActorRef client)
        {
            return new Get(Deadline, client);
        }
    }

    /// <summary>
    /// A request to receive the next inbox message matching a predicate before a deadline.
    /// </summary>
    internal struct Select : IQuery
    {
        /// <summary>
        /// Creates a request to receive a message matching a predicate.
        /// </summary>
        /// <param name="deadline">The monotonic-clock deadline for the request.</param>
        /// <param name="predicate">The predicate a message must satisfy to be selected.</param>
        /// <param name="client">The actor to receive the message, or <c>null</c> until the inbox actor records the sender.</param>
        public Select(TimeSpan deadline, Predicate<object> predicate, IActorRef client = null)
            : this()
        {
            Deadline = deadline;
            Predicate = predicate;
            Client = client;
        }

        /// <summary>
        /// The deadline by which a matching message must be received.
        /// </summary>
        public TimeSpan Deadline { get; private set; }
        /// <summary>
        /// The predicate used to select a message.
        /// </summary>
        public Predicate<object> Predicate { get; set; }
        /// <summary>
        /// The actor that will receive the selected message.
        /// </summary>
        public IActorRef Client { get; private set; }
        /// <summary>
        /// Copies this request with a different result recipient.
        /// </summary>
        /// <param name="client">The actor to receive the selected message.</param>
        /// <returns>A request with the same deadline and predicate and the supplied client.</returns>
        public IQuery WithClient(IActorRef client)
        {
            return new Select(Deadline, Predicate, client);
        }
    }

    /// <summary>
    /// A request for the inbox actor to begin watching a target actor.
    /// </summary>
    internal struct StartWatch
    {
        /// <summary>
        /// Creates a watch request, optionally carrying a custom termination message.
        /// </summary>
        /// <param name="target">The actor to monitor.</param>
        /// <param name="message">The message to deliver on termination, or <c>null</c> to receive <see cref="Terminated"/>.</param>
        public StartWatch(IActorRef target, object message)
            : this()
        {
            Target = target;
            Message = message;
        }

        /// <summary>
        /// The actor whose termination the inbox will monitor.
        /// </summary>
        public IActorRef Target { get; private set; }

        /// <summary>
        /// The custom termination message or null
        /// </summary>
        public object Message { get; private set; }
    }

    /// <summary>
    /// A request for the inbox actor to stop watching a target actor.
    /// </summary>
    internal struct StopWatch
    {
        /// <summary>
        /// Creates a request to stop watching the specified actor.
        /// </summary>
        /// <param name="target">The actor to stop monitoring.</param>
        public StopWatch(IActorRef target) 
            : this()
        {
            Target = target;
        }

        /// <summary>
        /// The actor the inbox will stop monitoring.
        /// </summary>
        public IActorRef Target { get; private set; }
    }

    internal struct Kick { }

    /// <summary>
    /// A linked-list-backed queue used by the inbox actor to remove messages by predicate.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the queue.</typeparam>
    [Serializable]
    internal class InboxQueue<T> : ICollection<T>
    {
        // LinkedList wrapper instead of Queue? While it's used for queueing, however I expect a lot of churn around 
        // adding-removing elements. Additionally we have to get a functionality of dequeueing element meeting
        // a specific predicate (even if it's in middle of queue), and current queue implementation won't provide that in easy way.


        private readonly LinkedList<T> _inner = new();

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            return _inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Appends an item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to append.</param>
        public void Add(T item)
        {
            _inner.AddLast(item);
        }

        /// <summary>
        /// Removes all items from the queue.
        /// </summary>
        public void Clear()
        {
            _inner.Clear();
        }

        /// <summary>
        /// Determines whether the queue contains the specified item.
        /// </summary>
        /// <param name="item">The item to find.</param>
        /// <returns><c>true</c> if the item is in the queue; otherwise, <c>false</c>.</returns>
        public bool Contains(T item)
        {
            return _inner.Contains(item);
        }

        /// <summary>
        /// Copies the queue's items to an array starting at the specified index.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _inner.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes the first occurrence of the specified item.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns><c>true</c> if an item was removed; otherwise, <c>false</c>.</returns>
        public bool Remove(T item)
        {
            return _inner.Remove(item);
        }

        /// <summary>
        /// Removes items from the head of the queue until an item satisfies the predicate.
        /// </summary>
        /// <param name="predicate">The predicate that stops removal when it returns <c>true</c>.</param>
        /// <returns>The number of items removed from the head of the queue.</returns>
        public int RemoveAll(Predicate<T> predicate)
        {
            var i = 0;
            var node = _inner.First;
            while (!(node == null || predicate(node.Value)))
            {
                var n = node;
                node = node.Next;
                _inner.Remove(n);
                i++;
            }

            return i;
        }

        /// <summary>
        /// Adds an item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        public void Enqueue(T item)
        {
            _inner.AddLast(item);
        }

        /// <summary>
        /// Removes and returns the item at the beginning of the queue.
        /// </summary>
        /// <returns>The item removed from the beginning of the queue.</returns>
        public T Dequeue()
        {
            var item = _inner.First.Value;
            _inner.RemoveFirst();
            return item;
        }

        /// <summary>
        /// Removes and returns the first item that matches the specified predicate, or the default value if no match is found.
        /// </summary>
        /// <param name="predicate">The predicate to match items against.</param>
        /// <returns>The first matching item, or the default value if no match is found.</returns>
        public T DequeueFirstOrDefault(Predicate<T> predicate)
        {
            var node = _inner.First;
            while (!(node == null || predicate(node.Value)))
            {
                node = node.Next;
            }

            if (node != null)
            {
                var item = node.Value;
                _inner.Remove(node);
                return item;
            }

            return default(T);
        }

        /// <summary>
        /// Gets the number of elements contained in the queue.
        /// </summary>
        public int Count { get { return _inner.Count; } }
        /// <summary>
        /// Gets a value indicating whether the queue is read-only.
        /// </summary>
        public bool IsReadOnly { get { return false; } }
    }

    /// <summary>
    /// Compares pending inbox queries by their monotonic-clock deadline.
    /// </summary>
    internal class DeadlineComparer : IComparer<IQuery>
    {
        /// <summary>
        /// The singleton instance of this comparer
        /// </summary>
        public static DeadlineComparer Instance { get; } = new();

        private DeadlineComparer()
        {
        }

        /// <inheritdoc/>
        public int Compare(IQuery x, IQuery y)
        {
            return x.Deadline.CompareTo(y.Deadline);
        }
    }

    /// <summary>
    /// <see cref="IInboxable"/> is an actor-like object to be listened by external objects.
    /// It can watch other actors lifecycle and contains inner actor, which could be passed
    /// as reference to other actors.
    /// </summary>
    public interface IInboxable : ICanWatch
    {
        /// <summary>
        /// Get a reference to internal actor. It may be for example registered in event stream.
        /// </summary>
        IActorRef Receiver { get; }

        /// <summary>
        /// Receive a next message from current <see cref="IInboxable"/> with default timeout. This call will return immediately,
        /// if the internal actor previously received a message, or will block until it'll receive a message.
        /// </summary>
        /// <returns>The next message received by the inbox.</returns>
        object Receive();

        /// <summary>
        /// Receive a next message from current <see cref="IInboxable"/>. This call will return immediately,
        /// if the internal actor previously received a message, or will block for time specified by 
        /// <paramref name="timeout"/> until it'll receive a message.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for a message.</param>
        /// <returns>The next message received by the inbox.</returns>
        object Receive(TimeSpan timeout);

        /// <summary>
        /// Waits asynchronously for the next message using the inbox's configured default timeout.
        /// </summary>
        /// <returns>A task that completes with the next message received by the inbox.</returns>
        Task<object> ReceiveAsync();

        /// <summary>
        /// Waits asynchronously for the next message until the specified timeout.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for a message.</param>
        /// <returns>A task that completes with the next message received by the inbox.</returns>
        Task<object> ReceiveAsync(TimeSpan timeout);

        /// <summary>
        /// Receive a next message satisfying specified <paramref name="predicate"/> under default timeout.
        /// </summary>
        /// <param name="predicate">The predicate a message must satisfy.</param>
        /// <returns>The next message that satisfies <paramref name="predicate"/>.</returns>
        object ReceiveWhere(Predicate<object> predicate);

        /// <summary>
        /// Receive a next message satisfying specified <paramref name="predicate"/> under provided <paramref name="timeout"/>.
        /// </summary>
        /// <param name="predicate">The predicate a message must satisfy.</param>
        /// <param name="timeout">The maximum time to wait for a matching message.</param>
        /// <returns>The next message that satisfies <paramref name="predicate"/>.</returns>
        object ReceiveWhere(Predicate<object> predicate, TimeSpan timeout);

        /// <summary>
        /// Makes an internal actor act as a proxy of a given <paramref name="message"/>, 
        /// which is sent to a given target actor. It means, that all <paramref name="target"/>'s
        /// replies will be sent to current inbox instead.
        /// </summary>
        /// <param name="target">The actor that should receive the message.</param>
        /// <param name="message">The message to forward with the inbox actor as its sender.</param>
        void Send(IActorRef target, object message);
    }

    /// <summary>
    /// Provides synchronous and asynchronous access to messages sent to a private inbox actor.
    /// </summary>
    public class Inbox : IInboxable, IDisposable
    {
        private static int _inboxNr = 0;
        private readonly ActorSystem _system;
        private readonly TimeSpan _defaultTimeout;

        /// <summary>
        /// Creates an inbox actor using the inbox settings from the actor system configuration.
        /// </summary>
        /// <param name="system">The actor system that hosts the inbox actor.</param>
        /// <returns>An inbox configured with the system's inbox size and default timeout.</returns>
        public static Inbox Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.actor.inbox");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<Inbox>("akka.actor.inbox");

            var inboxSize = config.GetInt("inbox-size", 0);
            var timeout = config.GetTimeSpan("default-timeout", null);

            var receiver = ((ActorSystemImpl)system).SystemActorOf(Props.Create(() => new InboxActor(inboxSize)), "inbox-" + Interlocked.Increment(ref _inboxNr));

            return new Inbox(timeout, receiver, system);
        }

        private Inbox(TimeSpan defaultTimeout, IActorRef receiver, ActorSystem system)
        {
            _defaultTimeout = defaultTimeout;
            _system = system;
            Receiver = receiver;
        }

        /// <summary>
        /// The actor reference used to receive messages and manage watched actors.
        /// </summary>
        public IActorRef Receiver { get; private set; }
        
        /// <summary>
        /// Make the inbox's actor watch the <paramref name="subject"/> actor such that 
        /// reception of the <see cref="Terminated"/> message can then be awaited.
        /// </summary>
        /// <param name="subject">The actor whose termination should be observed by this inbox.</param>
        /// <returns>The monitored actor reference.</returns>
        public IActorRef Watch(IActorRef subject)
        {
            Receiver.Tell(new StartWatch(subject, null));
            return subject;
        }

        public IActorRef WatchWith(IActorRef subject, object message)
        {
            Receiver.Tell(new StartWatch(subject, message));
            return subject;
        }

        /// <summary>
        /// Stops the inbox actor from monitoring the specified actor.
        /// </summary>
        /// <param name="subject">The actor to stop monitoring.</param>
        /// <returns>The actor reference that is no longer watched.</returns>
        public IActorRef Unwatch(IActorRef subject)
        {
            Receiver.Tell(new StopWatch(subject));
            return subject;
        }

        /// <summary>
        /// Forwards a message to a target actor with the inbox receiver as sender.
        /// </summary>
        /// <param name="actorRef">The actor that should receive the message.</param>
        /// <param name="message">The message to send.</param>
        public void Send(IActorRef actorRef, object message)
        {
            actorRef.Tell(message, Receiver);
        }

        /// <summary>
        /// Receive a single message from <see cref="Receiver"/> actor with default timeout. 
        /// NOTE: Timeout resolution depends on system's scheduler.
        /// </summary>
        /// <remarks>
        /// Don't use this method within actors, since it block current thread until a message is received.
        /// </remarks>
        /// <returns>The next message received by the inbox.</returns>
        public object Receive()
        {
            return Receive(_defaultTimeout);
        }

        /// <summary>
        /// Receive a single message from <see cref="Receiver"/> actor. 
        /// Provided <paramref name="timeout"/> is used for cleanup purposes.
        /// NOTE: <paramref name="timeout"/> resolution depends on system's scheduler.
        /// </summary>
        /// <remarks>
        /// Don't use this method within actors, since it block current thread until a message is received.
        /// </remarks>
        /// <param name="timeout">The maximum time to wait for a message.</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown if the inbox received a <see cref="Status.Failure"/> response message or
        /// it didn't receive a response message by the given <paramref name="timeout"/> .
        /// </exception>
        /// <returns>The next message received by the inbox.</returns>
        public object Receive(TimeSpan timeout)
        {
            var task = ReceiveAsync(timeout);
            return AwaitResult(task, timeout);
        }

        /// <summary>
        /// Receives the next message matching the predicate using the inbox's default timeout.
        /// </summary>
        /// <param name="predicate">The predicate a message must satisfy.</param>
        /// <returns>The next message that satisfies <paramref name="predicate"/>.</returns>
        public object ReceiveWhere(Predicate<object> predicate)
        {
            return ReceiveWhere(predicate, _defaultTimeout);
        }

        /// <summary>
        /// Receives the next message matching the predicate before the specified timeout.
        /// </summary>
        /// <param name="predicate">The predicate a message must satisfy.</param>
        /// <param name="timeout">The maximum time to wait for a matching message.</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown if the inbox received a <see cref="Status.Failure"/> response message or
        /// it didn't receive a response message by the given <paramref name="timeout"/> .
        /// </exception>
        /// <returns>The next message that satisfies <paramref name="predicate"/>.</returns>
        public object ReceiveWhere(Predicate<object> predicate, TimeSpan timeout)
        {
            var task = Receiver.Ask(new Select(_system.Scheduler.MonotonicClock + timeout, predicate), Timeout.InfiniteTimeSpan);
            return AwaitResult(task, timeout);
        }

        /// <summary>
        /// Waits asynchronously for the next message using the inbox's configured default timeout.
        /// </summary>
        /// <returns>A task that completes with the next message received by the inbox.</returns>
        public Task<object> ReceiveAsync()
        {
            return ReceiveAsync(_defaultTimeout);
        }

        /// <summary>
        /// Waits asynchronously for the next message until the specified timeout.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for a message.</param>
        /// <returns>A task that completes with the next message received by the inbox.</returns>
        public Task<object> ReceiveAsync(TimeSpan timeout)
        {
            return Receiver.Ask(new Get(_system.Scheduler.MonotonicClock + timeout), Timeout.InfiniteTimeSpan);
        }

      
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a 
        /// user's code. Managed and unmanaged resources will be disposed.<br />
        /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only 
        /// unmanaged resources can be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                _system.Stop(Receiver);
        }

        private object AwaitResult(Task<object> task, TimeSpan timeout)
        {
            if (!task.Wait(timeout))
                throw new TimeoutException(
                    $"Inbox {Receiver.Path} didn't receive a response message in specified timeout {timeout}");

            // Handle faulted tasks to avoid AggregateException when accessing task.Result
            if (task.IsFaulted)
            {
                var exception = task.Exception?.InnerException ?? task.Exception;
                if (exception is TimeoutException timeoutEx)
                    throw new TimeoutException(
                        $"Inbox {Receiver.Path} received a timeout: {timeoutEx.Message}", timeoutEx);
                throw exception!;
            }

            if (task.Result is Status.Failure received && received.Cause is TimeoutException)
            {
                throw new TimeoutException(
                    $"Inbox {Receiver.Path} received a status failure response message: {received.Cause.Message}", received.Cause);
            }

            return task.Result;
        }
    }
}
