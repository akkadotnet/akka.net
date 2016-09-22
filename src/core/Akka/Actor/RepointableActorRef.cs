//-----------------------------------------------------------------------
// <copyright file="RepointableActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Actor
{
    public class RepointableActorRef : ActorRefWithCell, IRepointableRef
    {
        private volatile ICell _underlying_DoNotCallMeDirectly;
        private volatile ICell _lookup_DoNotCallMeDirectly;
        protected readonly ActorSystemImpl System;
        protected readonly Props Props;
        protected readonly MessageDispatcher Dispatcher;
        internal readonly MailboxType MailboxType; // used in unit tests, hence why it's internal
        protected readonly IInternalActorRef Supervisor;
        protected readonly ActorPath _path;

        public RepointableActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, MailboxType mailboxType, IInternalActorRef supervisor, ActorPath path)
        {
            System = system;
            Props = props;
            Dispatcher = dispatcher;
            MailboxType = mailboxType;
            Supervisor = supervisor;
            _path = path;
        }


        public override ICell Underlying { get { return _underlying_DoNotCallMeDirectly; } }
        public ICell Lookup { get { return _lookup_DoNotCallMeDirectly; } }

        public override bool IsTerminated
        {
            get { return Underlying.IsTerminated; }
        }


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
        /// <exception cref="IllegalStateException">This exception is thrown if this function is called more than once.</exception>
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

        protected virtual ActorCell NewCell()
        {
            var actorCell = new ActorCell(System, this, Props, Dispatcher, Supervisor);
            actorCell.Init(false, MailboxType);
            return actorCell;
        }

        public override ActorPath Path { get { return _path; } }

        public override IInternalActorRef Parent { get { return Underlying.Parent; } }

        public override IActorRefProvider Provider { get { return System.Provider; } }

        public override bool IsLocal { get { return Underlying.IsLocal; } }



        public override void Start()
        {
            //Intentionally left blank
        }

        public override void Suspend()
        {
            Underlying.Suspend();
        }

        public override void SendSystemMessage(ISystemMessage message)
        {
            Underlying.SendSystemMessage(message);
        }

        public override void Resume(Exception causedByFailure = null)
        {
            Underlying.Resume(causedByFailure);
        }


        public override void Stop()
        {
            Underlying.Stop();
        }

        public override void Restart(Exception cause)
        {
            Underlying.Restart(cause);
        }

        /// <summary></summary>
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

        protected override void TellInternal(object message, IActorRef sender)
        {
            Underlying.SendMessage(sender, message);
        }

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
                    IChildStats stats;
                    if (Lookup.TryGetChildStatsByName(nameAndUid.Name, out stats))
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
                    return ActorRefs.Nobody;
            }
        }

        public override IInternalActorRef GetSingleChild(string name)
        {
            return Lookup.GetSingleChild(name);
        }

        public override IEnumerable<IActorRef> Children
        {
            get { return Lookup.GetChildren(); }
        }

    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
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

        public ActorSystem System { get { return _system; } }
        public ActorSystemImpl SystemImpl { get { return _system; } }
        public void Start()
        {
            //Akka does this. Not sure what it means. /HCanber
            //   this.type = this
        }

        public void Suspend()
        {
            SendSystemMessage(new Akka.Dispatch.SysMsg.Suspend());
        }

        public void Resume(Exception causedByFailure)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        public void Stop()
        {
            SendSystemMessage(new Terminate());
        }

        public IInternalActorRef Parent { get { return _supervisor; } }

        public IEnumerable<IInternalActorRef> GetChildren()
        {
            return Enumerable.Empty<IInternalActorRef>();
        }

        public IChildrenContainer ChildrenContainer => EmptyChildrenContainer.Instance;


        public IInternalActorRef GetSingleChild(string name)
        {
            return Nobody.Instance;
        }

        public IInternalActorRef GetChildByName(string name)
        {
            return Nobody.Instance;
        }

        public bool TryGetChildStatsByName(string name, out IChildStats child)
        {
            child = null;
            return false;
        }

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

        public bool IsLocal { get { return true; } }

        private bool CellIsReady(ICell cell)
        {
            return !ReferenceEquals(cell, this) && !ReferenceEquals(cell, null);
        }

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

        public IActorRef Self { get { return _self; } }
        public Props Props { get { return _props; } }
    }
}

