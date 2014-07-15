﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Actor
{
    public class RepointableActorRef : ActorRefWithCell, RepointableRef
    {
        private volatile Cell _underlying_DoNotCallMeDirectly;
        private volatile Cell _lookup_DoNotCallMeDirectly;
        private readonly ActorSystem _system;
        private readonly Props _props;
        private readonly MessageDispatcher _dispatcher;
        private readonly Func<Mailbox> _createMailbox;
        private readonly InternalActorRef _supervisor;
        private readonly ActorPath _path;

        public RepointableActorRef(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, InternalActorRef supervisor, ActorPath path)
        {
            _system = system;
            _props = props;
            _dispatcher = dispatcher;
            _createMailbox = createMailbox;
            _supervisor = supervisor;
            _path = path;
        }


        public override Cell Underlying { get { return _underlying_DoNotCallMeDirectly; } }
        public Cell Lookup { get { return _lookup_DoNotCallMeDirectly; } }



        public void SwapUnderlying(Cell cell)
        {
            #pragma warning disable 0420
            //Ok to ignore CS0420 "a reference to a volatile field will not be treated as volatile" for interlocked calls http://msdn.microsoft.com/en-us/library/4bw5ewxy(VS.80).aspx
            Interlocked.Exchange(ref _underlying_DoNotCallMeDirectly, cell);
            #pragma warning restore 0420
        }

        private void SwapLookup(Cell cell)
        {
            #pragma warning disable 0420
            //Ok to ignore CS0420 "a reference to a volatile field will not be treated as volatile" for interlocked calls http://msdn.microsoft.com/en-us/library/4bw5ewxy(VS.80).aspx
            Interlocked.Exchange(ref _lookup_DoNotCallMeDirectly, cell);
            #pragma warning restore 0420
        }

        ///<summary>
        ///Initialize: make a dummy cell which holds just a mailbox, then tell our
        ///supervisor that we exist so that he can create the real Cell in
        ///handleSupervise().
        ///Call twice on your own peril!
        ///This is protected so that others can have different initialization.
        /// </summary>
        public void Initialize(bool async)
        {
            var underlying = Underlying;
            if(underlying == null)
            {
                var newCell = new UnstartedCell(_system, this, _props, _supervisor);
                SwapUnderlying(newCell);
                SwapLookup(newCell);
                _supervisor.Tell(new Supervise(this, async));
                if(!async)
                    Point();
            }
            else
            {
                throw new IllegalStateException("initialize called more than once!");
            }
        }

        ///<summary>
        ///This method is supposed to be called by the supervisor in HandleSupervise()
        ///to replace the UnstartedCell with the real one. It assumes no concurrent
        ///modification of the `underlying` field, though it is safe to send messages
        ///at any time.
        /// </summary>
        public void Point()
        {
            var underlying = Underlying;
            if(underlying == null)
                throw new IllegalStateException("Underlying cell is null");

            var unstartedCell = underlying as UnstartedCell;
            if(unstartedCell != null)
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
            // underlying not beeing UnstartedCell happens routinely for things which were created async=false
        }

        protected virtual ActorCell NewCell()
        {
            var actorCell = new ActorCell(_system, this, _props, _dispatcher, _supervisor);
            actorCell.Init(sendSupervise: false, createMailbox: _createMailbox);
            return actorCell;
        }

        public override ActorPath Path { get { return _path; } }

        public override InternalActorRef Parent { get { return Underlying.Parent; } }

        public override ActorRefProvider Provider { get { return _system.Provider; } }

        public override bool IsLocal { get { return Underlying.IsLocal; } }



        public override void Start()
        {
            //Intentionally left blank
        }

        public override void Suspend()
        {
            Underlying.Suspend();
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

        public bool IsStarted
        {
            get
            {
                if(Underlying == null)
                    throw new IllegalStateException("IsStarted called before initialized");
                return !(Underlying is UnstartedCell);
            }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            Underlying.Post(sender, message);
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            var current = (ActorRef)this;
            var index = 0;
            foreach(var element in name)
            {
                switch(element)
                {
                    case "..":
                        return Parent.GetChild(name.Skip(index));
                    case "":
                        break;
                    default:
                        var nameAndUid = ActorCell.SplitNameAndUid(element);
                        var internalActorRef = Lookup.GetChildByName(nameAndUid.Name);
                        if(internalActorRef.IsNobody()) return Nobody;
                        return internalActorRef.GetChild(name.Skip(index));
                        //TODO: Implement this in order to be able to handle restarting children
                        //  lookup.getChildByName(childName) match {
                        //    case Some(crs: ChildRestartStats) if uid == ActorCell.undefinedUid || uid == crs.uid ⇒
                        //      crs.child.asInstanceOf[InternalActorRef].getChild(name)
                        //    case _ ⇒ Nobody
                        //  }
                }
                index++;
            }
            return current;
        }

        public override InternalActorRef GetSingleChild(string name)
        {
            return Lookup.GetSingleChild(name);
        }

        public override IEnumerable<ActorRef> Children
        {
            get { return Lookup.GetChildren(); }
        }

    }

    public class UnstartedCell : Cell
    {
        private readonly ActorSystem _system;
        private readonly RepointableActorRef _self;
        private readonly Props _props;
        private readonly InternalActorRef _supervisor;
        private readonly object _lock = new object();
        private readonly List<Envelope> _messageQueue = new List<Envelope>();
        private readonly TimeSpan _timeout;

        public UnstartedCell(ActorSystem system, RepointableActorRef self, Props props, InternalActorRef supervisor)
        {
            _system = system;
            _self = self;
            _props = props;
            _supervisor = supervisor;
            _timeout = _system.Settings.UnstartedPushTimeout;
        }

        public void ReplaceWith(Cell cell)
        {
            lock(_lock)
            {
                try
                {
                    foreach(var envelope in _messageQueue)
                    {
                        cell.Post(envelope.Sender, envelope.Message);
                    }
                }
                finally
                {
                    _self.SwapUnderlying(cell);
                }
            }
        }

        public ActorSystem System { get { return _system; } }
        public void Start()
        {
            //Akka does this. Not sure what it means. /HCanber
            //   this.type = this
        }

        public void Suspend()
        {
            SendSystemMessage(Akka.Dispatch.SysMsg.Suspend.Instance, ActorCell.GetCurrentSelfOrNoSender());
        }

        public void Resume(Exception causedByFailure)
        {
            SendSystemMessage(new Resume(causedByFailure), ActorCell.GetCurrentSelfOrNoSender());
        }

        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause), ActorCell.GetCurrentSelfOrNoSender());
        }

        public void Stop()
        {
            SendSystemMessage(Terminate.Instance, ActorCell.GetCurrentSelfOrNoSender());
        }

        public InternalActorRef Parent { get { return _supervisor; } }

        public IEnumerable<InternalActorRef> GetChildren()
        {
            return Enumerable.Empty<InternalActorRef>();
        }

        public InternalActorRef GetSingleChild(string name)
        {
            return Nobody.Instance;
        }

        public InternalActorRef GetChildByName(string name)
        {
            return Nobody.Instance;
        }

        public void Post(ActorRef sender, object message)
        {
            if(message is SystemMessage)
                SendSystemMessage(message, sender);
            else
                SendMessage(message, sender);
        }

        private void SendMessage(object message, ActorRef sender)
        {
            if(Monitor.TryEnter(_lock, _timeout))
            {
                try
                {
                    var cell = _self.Underlying;
                    if(CellIsReady(cell))
                    {
                        cell.Post(sender, message);
                    }
                    else
                    {
                        _messageQueue.Add(new Envelope { Message = message, Sender = sender });
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

        private void SendSystemMessage(object message, ActorRef sender)
        {
            lock(_lock)
            {
                var cell = _self.Underlying;
                if(CellIsReady(cell))
                {
                    cell.Post(sender, message);
                }
                else
                {
                    var envelope = new Envelope { Message = message, Sender = sender };
                    try
                    {
                        // systemMessages that are sent during replace need to jump to just after the last system message in the queue, so it's processed before other messages
                        if(!ReferenceEquals(_self.Lookup, this) && ReferenceEquals(_self.Underlying, this) &&
                           _messageQueue.Count != 0)
                            TryEnqueue(envelope);
                        else
                            _messageQueue.Add(envelope);
                        Mailbox.DebugPrint("{0} temp queueing system msg {1} from {2}", Self, message, sender);
                    }
                    catch(Exception e)
                    {
                        _system.EventStream.Publish(new Warning(_self.Path.ToString(), GetType(),
                            "Dropping message of type" + message.GetType() + " due to  enqueue failure: " + e.ToString()));
                        _system.DeadLetters.Tell(new DeadLetter(message, _self, _self), sender);
                    }
                }
            }
        }

        private void TryEnqueue(Envelope envelope)
        {
            var queueIndex = 0;
            var insertIntoIndex = -1;
            while(true)
            {
                var hasMoreMessagesInTheQueue = queueIndex < _messageQueue.Count;
                if(hasMoreMessagesInTheQueue)
                {
                    var queuedMessage = _messageQueue[queueIndex];
                    queueIndex++;
                    if(queuedMessage.Message is SystemMessage)
                        insertIntoIndex = queueIndex;
                }
                else if(insertIntoIndex == -1)
                {
                    _messageQueue.Add(envelope);
                    return;
                }
                else
                {
                    _messageQueue.Insert(insertIntoIndex, envelope);
                    return;
                }
            }
        }

        public bool IsLocal { get { return true; } }

        private bool CellIsReady(Cell cell)
        {
            return !ReferenceEquals(cell, this) && !ReferenceEquals(cell, null);
        }

        public bool HasMessages
        {
            get
            {
                lock(_lock)
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
                lock(_lock)
                {
                    var cell = _self.Underlying;
                    return CellIsReady(cell)
                        ? cell.NumberOfMessages
                        : _messageQueue.Count;
                }
            }
        }

        public ActorRef Self { get { return _self; } }
        public Props Props { get { return _props; } }
    }
}