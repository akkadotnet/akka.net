//-----------------------------------------------------------------------
// <copyright file="RepointableActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public class RepointableActorRef : ActorRefWithCell, IRepointableRef
    {
        private volatile ICell _underlying_DoNotCallMeDirectly;
        private volatile ICell _lookup_DoNotCallMeDirectly;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ActorSystemImpl System;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly Props Props;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly MessageDispatcher Dispatcher;
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly MailboxType MailboxType; // used in unit tests, hence why it's internal
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly IInternalActorRef Supervisor;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ActorPath _path;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="mailboxType">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="path">TBD</param>
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
        /// TBD
        /// </summary>
        public override ICell Underlying { get { return _underlying_DoNotCallMeDirectly; } }
        /// <summary>
        /// TBD
        /// </summary>
        public ICell Lookup { get { return _lookup_DoNotCallMeDirectly; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsTerminated
        {
            get { return Underlying.IsTerminated; }
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cell">TBD</param>
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
        /// <param name="async">TBD</param>
        /// <exception cref="IllegalStateException">This exception is thrown if this function is called more than once.</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected virtual ActorCell NewCell()
        {
            var actorCell = new ActorCell(System, this, Props, Dispatcher, Supervisor);
            actorCell.Init(false, MailboxType);
            return actorCell;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path { get { return _path; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Parent { get { return Underlying.Parent; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider { get { return System.Provider; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsLocal { get { return Underlying.IsLocal; } }



        /// <summary>
        /// TBD
        /// </summary>
        public override void Start()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Suspend()
        {
            Underlying.Suspend();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            Underlying.SendSystemMessage(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        public override void Resume(Exception causedByFailure = null)
        {
            Underlying.Resume(causedByFailure);
        }


        /// <summary>
        /// TBD
        /// </summary>
        public override void Stop()
        {
            Underlying.Stop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public override void Restart(Exception cause)
        {
            Underlying.Restart(cause);
        }

        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            Underlying.SendMessage(sender, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            var current = (IActorRef)this;
            if (!name.Any()) return current;

            var next = name.FirstOrDefault() ?? "";

            switch (next)
            {
                case "..":
                    return Parent.GetChild(name.Skip(1));
                case "":
                    return ActorRefs.Nobody;
                default:
                    var nameAndUid = ActorCell.SplitNameAndUid(next);
                    if (Lookup.TryGetChildStatsByName(nameAndUid.Name, out var stats))
                    {
                        var crs = stats as ChildRestartStats;
                        var uid = nameAndUid.Uid;
                        if (crs != null && (uid == ActorCell.UndefinedUid || uid == crs.Uid))
                        {
                            if (name.Skip(1).Any())
                                return crs.Child.GetChild(name.Skip(1));
                            else
                                return crs.Child;
                        }
                    }
                    else if (Lookup is ActorCell cell && cell.TryGetFunctionRef(nameAndUid.Name, nameAndUid.Uid, out var functionRef))
                    {
                        return functionRef;
                    }
                    return ActorRefs.Nobody;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IInternalActorRef GetSingleChild(string name)
        {
            return Lookup.GetSingleChild(name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IEnumerable<IActorRef> Children
        {
            get { return Lookup.GetChildren(); }
        }

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
        private readonly object _lock = new object();

        /* Both queues must be accessed via lock */
        private readonly LinkedList<Envelope> _messageQueue = new LinkedList<Envelope>();
        private LatestFirstSystemMessageList _sysMsgQueue = SystemMessageList.LNil;

        private readonly TimeSpan _timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="supervisor">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="cell">TBD</param>
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
        /// TBD
        /// </summary>
        public ActorSystem System { get { return _system; } }
        /// <summary>
        /// TBD
        /// </summary>
        public ActorSystemImpl SystemImpl { get { return _system; } }
        /// <summary>
        /// TBD
        /// </summary>
        public void Start()
        {
            //Akka does this. Not sure what it means. /HCanber
            //   this.type = this
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Suspend()
        {
            SendSystemMessage(new Akka.Dispatch.SysMsg.Suspend());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        public void Resume(Exception causedByFailure)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Stop()
        {
            SendSystemMessage(new Terminate());
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IInternalActorRef Parent { get { return _supervisor; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IInternalActorRef> GetChildren()
        {
            return Enumerable.Empty<IInternalActorRef>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IChildrenContainer ChildrenContainer => EmptyChildrenContainer.Instance;


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IInternalActorRef GetSingleChild(string name)
        {
            return Nobody.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IInternalActorRef GetChildByName(string name)
        {
            return Nobody.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetChildStatsByName(string name, out IChildStats child)
        {
            child = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sender">TBD</param>
        /// <param name="message">TBD</param>
        public void SendMessage(IActorRef sender, object message)
        {
            if (message is ISystemMessage)
                SendSystemMessage((ISystemMessage)message);
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
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
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
        /// TBD
        /// </summary>
        public bool IsLocal { get { return true; } }

        private bool CellIsReady(ICell cell)
        {
            return !ReferenceEquals(cell, this) && !ReferenceEquals(cell, null);
        }

        /// <summary>
        /// TBD
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
        /// TBD
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
        /// TBD
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
        /// TBD
        /// </summary>
        public IActorRef Self { get { return _self; } }
        /// <summary>
        /// TBD
        /// </summary>
        public Props Props { get { return _props; } }
    }
}

