//-----------------------------------------------------------------------
// <copyright file="RepointableActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Annotations;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;
using Akka.Util.Internal.Collections;

namespace Akka.Actor
{
    /// <summary>
    /// A reference to an actor that can be "repointed" to different actor cells during initialization.
    /// This is used for actors whose underlying implementation can change during startup.
    /// </summary>
    public class RepointableActorRef : ActorRefWithCell, IRepointableRef
    {
        private volatile ICell _underlying_DoNotCallMeDirectly;
        private volatile ICell _lookup_DoNotCallMeDirectly;
        /// <summary>
        /// The actor system that owns this actor reference.
        /// </summary>
        protected readonly ActorSystemImpl System;
        /// <summary>
        /// The props used to create the actor.
        /// </summary>
        protected readonly Props Props;
        /// <summary>
        /// The message dispatcher used by this actor.
        /// </summary>
        protected readonly MessageDispatcher Dispatcher;
        /// <summary>
        /// The mailbox type used by this actor.
        /// </summary>
        internal readonly MailboxType MailboxType; // used in unit tests, hence why it's internal
        /// <summary>
        /// The supervisor of this actor.
        /// </summary>
        protected readonly IInternalActorRef Supervisor;
        /// <summary>
        /// The actor path of this actor.
        /// </summary>
        protected readonly ActorPath _path;

        /// <summary>
        /// Creates a new RepointableActorRef with the specified parameters.
        /// </summary>
        /// <param name="system">The actor system that owns this actor reference.</param>
        /// <param name="props">The props used to create the actor.</param>
        /// <param name="dispatcher">The message dispatcher used by this actor.</param>
        /// <param name="mailboxType">The mailbox type used by this actor.</param>
        /// <param name="supervisor">The supervisor of this actor.</param>
        /// <param name="path">The actor path of this actor.</param>
        public RepointableActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, MailboxType mailboxType, IInternalActorRef supervisor, ActorPath path)
        {
            System = system;
            Props = props;
            Dispatcher = dispatcher;
            MailboxType = mailboxType;
            Supervisor = supervisor;
            _path = path;
        }

        /// <summary>
        /// Gets the underlying cell for this actor reference.
        /// </summary>
        public override ICell Underlying { get { return _underlying_DoNotCallMeDirectly; } }
        /// <summary>
        /// Gets the lookup cell for this actor reference, which is used for child lookup operations.
        /// </summary>
        public ICell Lookup { get { return _lookup_DoNotCallMeDirectly; } }

        /// <summary>
        /// Indicates whether this actor reference is terminated.
        /// </summary>
        public override bool IsTerminated
        {
            get { return Underlying.IsTerminated; }
        }


        /// <summary>
        /// Swaps the underlying cell of this actor reference with the provided cell.
        /// </summary>
        /// <param name="cell">The new cell to use.</param>
        public void SwapUnderlying(ICell cell)
        {
#pragma warning disable 0420
            //Ok to ignore CS0420 "a reference to a volatile field will not be treated as volatile" for interlocked calls http://msdn.microsoft.com/en-us/library/4bw5ewxy(VS.80).aspx
            Interlocked.Exchange(ref _underlying_DoNotCallMeDirectly, cell);
#pragma warning restore 0420
        }

        private void SwapLookup(ICell cell)
        {
#pragma warning disable 0420
            //Ok to ignore CS0420 "a reference to a volatile field will not be treated as volatile" for interlocked calls http://msdn.microsoft.com/en-us/library/4bw5ewxy(VS.80).aspx
            Interlocked.Exchange(ref _lookup_DoNotCallMeDirectly, cell);
#pragma warning restore 0420
        }

        /// <summary>
        /// Initialize: make a dummy cell which holds just a mailbox, then tell our
        /// supervisor that we exist so that he can create the real Cell in
        /// handleSupervise().
        /// </summary>
        /// <param name="async">Whether to initialize asynchronously or synchronously.</param>
        /// <exception cref="IllegalStateException">This exception is thrown if this function is called more than once.</exception>
        /// <returns>This actor reference for fluent chaining.</returns>
        public RepointableActorRef Initialize(bool async)
        {
            var underlying = Underlying;
            if (underlying == null)
            {
                var newCell = new UnstartedCell(System, this, Props, Supervisor);
                SwapUnderlying(newCell);
                SwapLookup(newCell);
                Supervisor.SendSystemMessage(new Supervise(this, async));
                if (!async)
                    Point();

                return this;
            }
            else
            {
                throw new IllegalStateException("initialize called more than once!");
            }
        }

        /// <summary>
        /// This method is supposed to be called by the supervisor in HandleSupervise()
        /// to replace the UnstartedCell with the real one. It assumes no concurrent
        /// modification of the `underlying` field, though it is safe to send messages
        /// at any time.
        /// </summary>
        /// <exception cref="IllegalStateException">This exception is thrown if the underlying cell is undefined.</exception>
        public void Point()
        {
            var underlying = Underlying;
            if (underlying == null)
                throw new IllegalStateException("Underlying cell is null");

            var unstartedCell = underlying as UnstartedCell;
            if (unstartedCell != null)
            {
                // The problem here was that if the real actor (which will start running
                // at cell.start()) creates children in its constructor, then this may
                // happen before the swapCell in u.replaceWith, meaning that those
                // children cannot be looked up immediately, e.g. if they shall become
                // routees.
                var cell = NewCell();
                SwapLookup(cell);
                cell.Start();
                unstartedCell.ReplaceWith(cell);
            }
            // underlying not being UnstartedCell happens routinely for things which were created async=false
        }

        /// <summary>
        /// Creates a new ActorCell for this actor reference.
        /// </summary>
        /// <returns>The created ActorCell.</returns>
        protected virtual ActorCell NewCell()
        {
            var actorCell = new ActorCell(System, this, Props, Dispatcher, Supervisor);
            actorCell.Init(false, MailboxType);
            return actorCell;
        }

        /// <summary>
        /// Gets the path of this actor reference.
        /// </summary>
        public override ActorPath Path { get { return _path; } }

        /// <summary>
        /// Stops the actor by sending a <see cref="Terminate"/> system message.
        /// </summary>
        public override void Stop()
        {
            Underlying.Stop();
        }

        /// <summary>
        /// Sends a system message to the underlying actor cell.
        /// </summary>
        /// <param name="message">The system message to send.</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            Underlying.SendSystemMessage(message);
        }

        /// <summary>
        /// Resumes the actor after being suspended.
        /// </summary>
        /// <param name="causedByFailure">The exception that caused the actor to be suspended, if any.</param>
        public override void Resume(Exception causedByFailure = null)
        {
            Underlying.Resume(causedByFailure);
        }

        /// <summary>
        /// Suspends the actor temporarily by sending a Suspend system message.
        /// </summary>
        public override void Suspend()
        {
            Underlying.Suspend();
        }

        /// <summary>
        /// Restarts the actor by sending a Recreate system message with the specified cause.
        /// </summary>
        /// <param name="cause">The exception that caused the restart.</param>
        public override void Restart(Exception cause)
        {
            Underlying.Restart(cause);
        }

        /// <summary>
        /// Indicates whether this actor has been started and is no longer an UnstartedCell.
        /// </summary>
        /// <exception cref="IllegalStateException">This exception is thrown if this property is called before actor is initialized (<see cref="Initialize(bool)"/>).</exception>
        public bool IsStarted
        {
            get
            {
                if (Underlying == null)
                    throw new IllegalStateException("IsStarted called before initialized");
                return !(Underlying is UnstartedCell);
            }
        }

        /// <summary>
        /// Sends a message to the underlying actor.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender of the message.</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            Underlying.SendMessage(sender, message);
        }

        /// <summary>
        /// Retrieves a child actor by path elements.
        /// </summary>
        /// <param name="name">The path elements to the child.</param>
        /// <returns>The child actor reference, or Nobody if no match is found.</returns>
        public override IActorRef GetChild(IReadOnlyList<string> name)
        {
            if (name.Count == 0) return this;

            var next = name[0];

            switch (next)
            {
                case "..":
                    return Parent.GetChild(name.NoCopySlice(1));
                case "":
                    return ActorRefs.Nobody;
                default:
                    var (s, uid) = ActorCell.GetNameAndUid(next);
                    if (Lookup.TryGetChildStatsByName(s, out var stats))
                    {
                        if (stats is ChildRestartStats crs && (uid == ActorCell.UndefinedUid || uid == crs.Uid))
                        {
                            if (name.Count > 1)
                                return crs.Child.GetChild(name.NoCopySlice(1));
                            else
                                return crs.Child;
                        }
                    }
                    else if (Lookup is ActorCell cell && cell.TryGetFunctionRef(s, uid, out var functionRef))
                    {
                        return functionRef;
                    }
                    return ActorRefs.Nobody;
            }
        }

        /// <summary>
        /// Gets a single child actor by name.
        /// </summary>
        /// <param name="name">The name of the child.</param>
        /// <returns>The child actor reference, or Nobody if no match is found.</returns>
        public override IInternalActorRef GetSingleChild(string name)
        {
            return Lookup.GetSingleChild(name);
        }

        /// <summary>
        /// Gets an enumeration of all child actors.
        /// </summary>
        public override IEnumerable<IActorRef> Children
        {
#pragma warning disable CS0618 // Type or member is obsolete
            get { return Lookup.GetChildren(); }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public override IInternalActorRef Parent => Supervisor;
        public override bool IsLocal => true;
        public override IActorRefProvider Provider => System.Provider;
        public override void Start() { /* No-op for RepointableActorRef */ }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public class UnstartedCell : ICell
    {
        private readonly ActorSystemImpl _system;
        private readonly RepointableActorRef _self;
        private readonly Props _props;
        private readonly IInternalActorRef _supervisor;
        private readonly object _lock = new();

        /* Both queues must be accessed via lock */
        private readonly LinkedList<Envelope> _messageQueue = new();
        private LatestFirstSystemMessageList _sysMsgQueue = SystemMessageList.LNil;

        private readonly TimeSpan _timeout;

        /// <summary>
        /// Creates a new UnstartedCell for a RepointableActorRef.
        /// </summary>
        /// <param name="system">The actor system that owns this cell.</param>
        /// <param name="self">The actor reference that this cell belongs to.</param>
        /// <param name="props">The props used to create the actor.</param>
        /// <param name="supervisor">The supervisor of this actor.</param>
        public UnstartedCell(ActorSystemImpl system, RepointableActorRef self, Props props, IInternalActorRef supervisor)
        {
            _system = system;
            _self = self;
            _props = props;
            _supervisor = supervisor;
            _timeout = _system.Settings.UnstartedPushTimeout;
        }

        private void DrainSysMsgQueue(ICell cell)
        {
            while (_sysMsgQueue.NonEmpty)
            {
                var sysQ = _sysMsgQueue.Reverse;
                _sysMsgQueue = SystemMessageList.LNil;
                while (sysQ.NonEmpty)
                {
                    var msg = sysQ.Head;
                    sysQ = sysQ.Tail;
                    msg.Unlink();
                    cell.SendSystemMessage(msg);
                }
            }
        }

        /// <summary>
        /// Replaces this UnstartedCell with a real Cell, transferring any queued messages.
        /// </summary>
        /// <param name="cell">The Cell to replace this one with.</param>
        public void ReplaceWith(ICell cell)
        {
            lock (_lock)
            {
                try
                {
                    DrainSysMsgQueue(cell);

                    while (_messageQueue.Count > 0)
                    {
                        // roughly equal to what "poll" does
                        var e = _messageQueue.First.Value;
                        _messageQueue.RemoveFirst();
                        cell.SendMessage(e.Sender, e.Message);

                        // drain sysmsgQueue in case a msg enqueues a sys msg
                        DrainSysMsgQueue(cell);
                    }
                }
                finally
                {
                    _self.SwapUnderlying(cell);
                }
            }
        }

        /// <summary>
        /// Gets the actor system that owns this cell.
        /// </summary>
        public ActorSystem System { get { return _system; } }
        /// <summary>
        /// Gets the actor system implementation that owns this cell.
        /// </summary>
        public ActorSystemImpl SystemImpl { get { return _system; } }
        /// <summary>
        /// No-op for UnstartedCell.
        /// </summary>
        public void Start()
        {
            //Akka does this. Not sure what it means. /HCanber
            //   this.type = this
        }

        /// <summary>
        /// Suspends the actor by sending a Suspend system message.
        /// </summary>
        public void Suspend()
        {
            SendSystemMessage(new Akka.Dispatch.SysMsg.Suspend());
        }

        /// <summary>
        /// Resumes the actor by sending a Resume system message.
        /// </summary>
        /// <param name="causedByFailure">The exception that caused the actor to be suspended.</param>
        public void Resume(Exception causedByFailure)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        /// <summary>
        /// Restarts the actor by sending a Recreate system message.
        /// </summary>
        /// <param name="cause">The exception that caused the actor to be restarted.</param>
        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        /// <summary>
        /// Stops the actor by sending a Terminate system message.
        /// </summary>
        public void Stop()
        {
            SendSystemMessage(new Terminate());
        }

        /// <summary>
        /// Gets the parent actor reference.
        /// </summary>
        public IInternalActorRef Parent { get { return _supervisor; } }

        /// <summary>
        /// Returns an empty enumeration of children for this actor reference.
        /// </summary>
        /// <returns>An empty enumeration of <see cref="IInternalActorRef"/>.</returns>
        public IEnumerable<IInternalActorRef> GetChildren()
        {
            return Enumerable.Empty<IInternalActorRef>();
        }

        /// <summary>
        /// Gets the children container, which is always empty for this reference.
        /// </summary>
        public IChildrenContainer ChildrenContainer => EmptyChildrenContainer.Instance;

        /// <summary>
        /// Returns <see cref="Nobody.Instance"/> for any child name.
        /// </summary>
        /// <param name="name">The child name.</param>
        /// <returns>Always <see cref="Nobody.Instance"/>.</returns>
        public IInternalActorRef GetSingleChild(string name)
        {
            return Nobody.Instance;
        }

        /// <summary>
        /// Returns <see cref="Nobody.Instance"/> for any child name.
        /// </summary>
        /// <param name="name">The child name.</param>
        /// <returns>Always <see cref="Nobody.Instance"/>.</returns>
        public IInternalActorRef GetChildByName(string name)
        {
            return Nobody.Instance;
        }

        /// <summary>
        /// Always returns false and sets <paramref name="child"/> to null.
        /// </summary>
        /// <param name="name">The child name.</param>
        /// <param name="child">The output child stats (always null).</param>
        /// <returns>Always false.</returns>
        public bool TryGetChildStatsByName(string name, out IChildStats child)
        {
            child = null;
            return false;
        }

        /// <summary>
        /// Sends a message to this actor reference, dispatching system messages appropriately.
        /// </summary>
        /// <param name="sender">The sender actor reference.</param>
        /// <param name="message">The message to send.</param>
        public void SendMessage(IActorRef sender, object message)
        {
            if (message is ISystemMessage systemMessage)
                SendSystemMessage(systemMessage);
            else
                SendMessage(message, sender);
        }

        private void SendMessage(object message, IActorRef sender)
        {
            if (Monitor.TryEnter(_lock, _timeout))
            {
                try
                {
                    var cell = _self.Underlying;
                    if (CellIsReady(cell))
                    {
                        cell.SendMessage(sender, message);
                    }
                    else
                    {
                        _messageQueue.AddLast(new Envelope(message, sender));
                        Mailbox.DebugPrint("{0} temp queueing {1} from {2}", Self, message, sender);
                    }
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
            else
            {
                _system.EventStream.Publish(new Warning(_self.Path.ToString(), GetType(), "Dropping message of type" + message.GetType() + " due to lock timeout"));
                _system.DeadLetters.Tell(new DeadLetter(message, sender, _self), sender);
            }
        }

        /// <summary>
        /// Sends a system message to the underlying cell or queues it if the cell is not ready.
        /// </summary>
        /// <param name="message">The system message to send.</param>
        public void SendSystemMessage(ISystemMessage message)
        {
            lock (_lock)
            {
                var cell = _self.Underlying;
                if (CellIsReady(cell))
                {
                    cell.SendSystemMessage(message);
                }
                else
                {
                    _sysMsgQueue = _sysMsgQueue + (SystemMessage)message;
                    Mailbox.DebugPrint("{0} temp queueing system message {1}", Self, message);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this actor reference is local.
        /// </summary>
        public bool IsLocal { get { return true; } }

        private bool CellIsReady(ICell cell)
        {
            return !ReferenceEquals(cell, this) && !ReferenceEquals(cell, null);
        }

        /// <summary>
        /// Gets a value indicating whether this actor reference is terminated.
        /// </summary>
        public bool IsTerminated
        {
            get
            {
                lock (_lock)
                {
                    var cell = _self.Underlying;
                    return CellIsReady(cell) && cell.IsTerminated;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this actor reference has messages in its queue.
        /// </summary>
        public bool HasMessages
        {
            get
            {
                lock (_lock)
                {
                    var cell = _self.Underlying;
                    return CellIsReady(cell)
                        ? cell.HasMessages
                        : _messageQueue.Count > 0;
                }
            }
        }

        /// <summary>
        /// Gets the number of messages in the queue for this actor reference.
        /// </summary>
        public int NumberOfMessages
        {
            get
            {
                lock (_lock)
                {
                    var cell = _self.Underlying;
                    return CellIsReady(cell)
                        ? cell.NumberOfMessages
                        : _messageQueue.Count;
                }
            }
        }

        /// <summary>
        /// Gets the actor reference itself.
        /// </summary>
        public IActorRef Self { get { return _self; } }
        /// <summary>
        /// Gets the <see cref="Props"/> used to create this actor reference.
        /// </summary>
        public Props Props { get { return _props; } }
    }
}

