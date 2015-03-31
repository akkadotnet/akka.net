using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internals;

namespace Akka.Actor
{
    internal interface IQuery
    {
        DateTime Deadline { get; }
        IActorRef Client { get; }
        IQuery WithClient(IActorRef client);
    }

    internal struct Get : IQuery
    {
        public Get(DateTime deadline, IActorRef client = null)
            : this()
        {
            Deadline = deadline;
            Client = client;
        }

        public DateTime Deadline { get; private set; }
        public IActorRef Client { get; private set; }
        public IQuery WithClient(IActorRef client)
        {
            return new Get(Deadline, client);
        }
    }

    internal struct Select : IQuery
    {
        public Select(DateTime deadline, Predicate<object> predicate, IActorRef client = null)
            : this()
        {
            Deadline = deadline;
            Predicate = predicate;
            Client = client;
        }

        public DateTime Deadline { get; private set; }
        public Predicate<object> Predicate { get; set; }
        public IActorRef Client { get; private set; }
        public IQuery WithClient(IActorRef client)
        {
            return new Select(Deadline, Predicate, client);
        }
    }

    internal struct StartWatch
    {
        public StartWatch(IActorRef target)
            : this()
        {
            Target = target;
        }

        public IActorRef Target { get; private set; }
    }

    internal struct StopWatch
    {
        public StopWatch(IActorRef target) 
            : this()
        {
            Target = target;
        }

        public IActorRef Target { get; private set; }
    }

    internal struct Kick { }

    // LinkedList wrapper instead of Queue? While it's used for queueing, however I expect a lot of churn around 
    // adding-removing elements. Additionally we have to get a functionality of dequeueing element meeting
    // a specific predicate (even if it's in middle of queue), and current queue implementation won't provide that in easy way.
    [Serializable]
    internal class InboxQueue<T> : ICollection<T>
    {
        private readonly LinkedList<T> _inner = new LinkedList<T>();

        public IEnumerator<T> GetEnumerator()
        {
            return _inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            _inner.AddLast(item);
        }

        public void Clear()
        {
            _inner.Clear();
        }

        public bool Contains(T item)
        {
            return _inner.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _inner.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return _inner.Remove(item);
        }

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

        public void Enqueue(T item)
        {
            _inner.AddLast(item);
        }

        public T Dequeue()
        {
            var item = _inner.First.Value;
            _inner.RemoveFirst();
            return item;
        }

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

        public int Count { get { return _inner.Count; } }
        public bool IsReadOnly { get { return false; } }
    }

    internal class DeadlineComparer : IComparer<IQuery>
    {
        private static readonly DeadlineComparer _instance = new DeadlineComparer();

        public static DeadlineComparer Instance { get { return _instance; } }

        private DeadlineComparer()
        {
        }

        public int Compare(IQuery x, IQuery y)
        {
            return x.Deadline.CompareTo(y.Deadline);
        }
    }

    /// <summary>
    /// <see cref="Inboxable"/> is an actor-like object to be listened by external objects.
    /// It can watch other actors lifecycle and contains inner actor, which could be passed
    /// as reference to other actors.
    /// </summary>
    public interface Inboxable : ICanWatch
    {
        /// <summary>
        /// Get a reference to internal actor. It may be for example registered in event stream.
        /// </summary>
        IActorRef Receiver { get; }

        /// <summary>
        /// Receive a next message from current <see cref="Inboxable"/> with default timeout. This call will return immediately,
        /// if the internal actor previously received a message, or will block until it'll receive a message.
        /// </summary>
        object Receive();

        /// <summary>
        /// Receive a next message from current <see cref="Inboxable"/>. This call will return immediately,
        /// if the internal actor previously received a message, or will block for time specified by 
        /// <paramref name="timeout"/> until it'll receive a message.
        /// </summary>
        object Receive(TimeSpan timeout);

        Task<object> ReceiveAsync();

        Task<object> ReceiveAsync(TimeSpan timeout);

        /// <summary>
        /// Receive a next message satisfying specified <paramref name="predicate"/> under default timeout.
        /// </summary>
        object ReceiveWhere(Predicate<object> predicate);

        /// <summary>
        /// Receive a next message satisfying specified <paramref name="predicate"/> under provided <paramref name="timeout"/>.
        /// </summary>
        object ReceiveWhere(Predicate<object> predicate, TimeSpan timeout);

        /// <summary>
        /// Makes an internal actor act as a proxy of given <paramref name="message"/>, 
        /// which will be send to given <paramref cref="target"/> actor. It means, 
        /// that all <paramref name="target"/>'s replies will be sent to current inbox instead.
        /// </summary>
        void Send(IActorRef target, object message);
    }

    public class Inbox : Inboxable, IDisposable
    {
        private static int inboxNr = 0;
        private readonly ISet<IObserver<object>> _subscribers;
        private readonly ActorSystem _system;
        private readonly TimeSpan _defaultTimeout;

        public static Inbox Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka").GetConfig("actor").GetConfig("inbox");
            var inboxSize = config.GetInt("inbox-size");
            var timeout = config.GetTimeSpan("default-timeout");

            var receiver =((ActorSystemImpl) system).SystemActorOf(Props.Create(() => new InboxActor(inboxSize)), "inbox-" + Interlocked.Increment(ref inboxNr));

            var inbox = new Inbox(timeout, receiver, system);
            return inbox;
        }

        private Inbox(TimeSpan defaultTimeout, IActorRef receiver, ActorSystem system)
        {
            _subscribers = new HashSet<IObserver<object>>();
            _defaultTimeout = defaultTimeout;
            _system = system;
            Receiver = receiver;
        }

        public IActorRef Receiver { get; private set; }
        
        /// <summary>
        /// Make the inbox’s actor watch the <paramref name="subject"/> actor such that 
        /// reception of the <see cref="Terminated"/> message can then be awaited.
        /// </summary>
        public IActorRef Watch(IActorRef subject)
        {
            Receiver.Tell(new StartWatch(subject));
            return subject;
        }

        public IActorRef Unwatch(IActorRef subject)
        {
            Receiver.Tell(new StopWatch(subject));
            return subject;
        }

        public void Send(IActorRef actorRef, object msg)
        {
            actorRef.Tell(msg, Receiver);
        }

        /// <summary>
        /// Receive a single message from <see cref="Receiver"/> actor with default timeout. 
        /// NOTE: Timeout resolution depends on system's scheduler.
        /// </summary>
        /// <remarks>
        /// Don't use this method within actors, since it block current thread until a message is received.
        /// </remarks>
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
        public object Receive(TimeSpan timeout)
        {
            var task = ReceiveAsync(timeout);
            return AwaitResult(task, timeout);
        }

        public object ReceiveWhere(Predicate<object> predicate)
        {
            return ReceiveWhere(predicate, _defaultTimeout);
        }

        public object ReceiveWhere(Predicate<object> predicate, TimeSpan timeout)
        {
            var task = Receiver.Ask(new Select(DateTime.Now + timeout, predicate), Timeout.InfiniteTimeSpan);
            return AwaitResult(task, timeout);
        }

        public Task<object> ReceiveAsync()
        {
            return ReceiveAsync(_defaultTimeout);
        }

        public Task<object> ReceiveAsync(TimeSpan timeout)
        {
            return Receiver.Ask(new Get(DateTime.Now + timeout), Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                _system.Stop(Receiver);
        }

        private object AwaitResult(Task<object> task, TimeSpan timeout)
        {
            if (task.Wait(timeout))
            {
                return task.Result;
            }
            else
            {
                var fmt = string.Format("Inbox {0} didn't received a response message in specified timeout {1}", Receiver.Path, timeout);
                throw new TimeoutException(fmt);
            }
        }
    }
}