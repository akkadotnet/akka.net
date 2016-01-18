//-----------------------------------------------------------------------
// <copyright file="ActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;

namespace Akka.Actor
{
    public partial class ActorCell : IUntypedActorContext, ICell 
    {
        /// <summary>NOTE! Only constructor and ClearActorFields is allowed to update this</summary>
        private IInternalActorRef _self;
        public const int UndefinedUid = 0;
        private Props _props;
        private static readonly Props terminatedProps=new TerminatedProps();


        private long _uid;
        private ActorBase _actor;
        private bool _actorHasBeenCleared;
        private Mailbox _mailbox;
        private readonly ActorSystemImpl _systemImpl;
        private ActorTaskScheduler _taskScheduler;


        public ActorCell(ActorSystemImpl system, IInternalActorRef self, Props props, MessageDispatcher dispatcher, IInternalActorRef parent)
        {
            _self = self;
            _props = props;
            _systemImpl = system;
            Parent = parent;
            Dispatcher = dispatcher;
            
        }

        public object CurrentMessage { get; private set; }
        public Mailbox Mailbox { get { return _mailbox; } }

        public MessageDispatcher Dispatcher { get; private set; }
        public bool IsLocal { get{return true;} }
        protected ActorBase Actor { get { return _actor; } }
        public bool IsTerminated { get { return ReferenceEquals(_systemImpl.Mailboxes.DeadLetterMailbox,_mailbox) || _mailbox.IsClosed; } }
        internal static ActorCell Current
        {
            get { return InternalCurrentActorCellKeeper.Current; }
        }

        public ActorSystem System { get { return _systemImpl; } }
        public ActorSystemImpl SystemImpl { get { return _systemImpl; } }
        public Props Props { get { return _props; } }
        public IActorRef Self { get { return _self; } }
        IActorRef IActorContext.Parent { get { return Parent; } }
        public IInternalActorRef Parent { get; private set; }
        public IActorRef Sender { get; private set; }
        public bool HasMessages { get { return Mailbox.HasUnscheduledMessages; } }
        public int NumberOfMessages { get { return Mailbox.NumberOfMessages; } }
        internal bool ActorHasBeenCleared { get { return _actorHasBeenCleared; } }
        internal static Props TerminatedProps { get { return terminatedProps; } }

        public ActorTaskScheduler TaskScheduler
        {
            get
            {
                var taskScheduler = Volatile.Read(ref _taskScheduler);

                if (taskScheduler != null)
                    return taskScheduler;

                taskScheduler = new ActorTaskScheduler(this);
                return Interlocked.CompareExchange(ref _taskScheduler, taskScheduler, null) ?? taskScheduler;
            }
        }

        public void Init(bool sendSupervise, Func<Mailbox> createMailbox /*, MailboxType mailboxType*/) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType
        {
            var mailbox = createMailbox(); //Akka: dispatcher.createMailbox(this, mailboxType)
            Dispatcher.Attach(this);
            mailbox.Setup(Dispatcher);
            mailbox.SetActor(this);
            _mailbox = mailbox;

            var createMessage = new Create();
            // AKKA:
            //   /*
            //    * The mailboxType was calculated taking into account what the MailboxType
            //    * has promised to produce. If that was more than the default, then we need
            //    * to reverify here because the dispatcher may well have screwed it up.
            //    */
            //// we need to delay the failure to the point of actor creation so we can handle
            //// it properly in the normal way
            //val actorClass = props.actorClass
            //val createMessage = mailboxType match {
            //    case _: ProducesMessageQueue[_] if system.mailboxes.hasRequiredType(actorClass) ⇒
            //    val req = system.mailboxes.getRequiredType(actorClass)
            //    if (req isInstance mbox.messageQueue) Create(None)
            //    else {
            //        val gotType = if (mbox.messageQueue == null) "null" else mbox.messageQueue.getClass.getName
            //        Create(Some(ActorInitializationException(self,
            //        s"Actor [$self] requires mailbox type [$req] got [$gotType]")))
            //    }
            //    case _ ⇒ Create(None)
            //}

            //swapMailbox(mbox)
            //mailbox.setActor(this)

            //// ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
            //mailbox.systemEnqueue(self, createMessage)
            var self = Self;
            mailbox.Post(self, new Envelope {Message = createMessage, Sender = self});

            if(sendSupervise)
            {
                Parent.Tell(new Supervise(self, async: false), self);
            }
        }

        [Obsolete("Use TryGetChildStatsByName", true)]
        public IInternalActorRef GetChildByName(string name)   //TODO: Should return  Option[ChildStats]
        {
            IInternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRefs.Nobody;
        }

        IActorRef IActorContext.Child(string name)
        {
            IInternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRefs.Nobody;
        }

        public ActorSelection ActorSelection(string path)
        {
            return ActorRefFactoryShared.ActorSelection(path, _systemImpl, Self);
        }

        public ActorSelection ActorSelection(ActorPath path)
        {
            return ActorRefFactoryShared.ActorSelection(path, _systemImpl);
        }


        IEnumerable<IActorRef> IActorContext.GetChildren()
        {
            return GetChildren();
        }

        public IEnumerable<IInternalActorRef> GetChildren()
        {
            return ChildrenContainer.Children;
        }

        public void Become(Receive receive)
        {
            _state = _state.Become(receive);
        }

        public void BecomeStacked(Receive receive)
        {
            _state = _state.BecomeStacked(receive);
        }


        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        void IActorContext.Become(Receive receive, bool discardOld = true)
        {
            if(discardOld)
                Become(receive);
            else
                BecomeStacked(receive);
        }

        [Obsolete("Use UnbecomeStacked instead. This method will be removed in future versions")]
        void IActorContext.Unbecome()
        {
            UnbecomeStacked();
        }

        public void UnbecomeStacked()
        {
            _state = _state.UnbecomeStacked();
        }

        void IUntypedActorContext.Become(UntypedReceive receive)
        {
            Become(m => { receive(m); return true; });
        }

        void IUntypedActorContext.BecomeStacked(UntypedReceive receive)
        {
            BecomeStacked(m => { receive(m); return true; });
        }

        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        void IUntypedActorContext.Become(UntypedReceive receive, bool discardOld)
        {
            if (discardOld)
                Become(m => { receive(m); return true; });
            else
                BecomeStacked(m => { receive(m); return true; });
        }

        private long NewUid()
        {
            var auid = Interlocked.Increment(ref _uid);
            return auid;
        }

        private ActorBase NewActor()
        {
            PrepareForNewActor();
            ActorBase instance=null;
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                _state = _state.ClearBehaviorStack();
                instance = CreateNewActorInstance();
                //TODO: this overwrites any already initialized supervisor strategy
                //We should investigate what we can do to handle this better
                instance.SupervisorStrategyInternal = _props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantiation unless explicitly set in props
            });
            return instance;
        }

        protected virtual ActorBase CreateNewActorInstance()
        {
            var actor = _props.NewActor();

            // Apply default of custom behaviors to actor.
            var pipeline = _systemImpl.ActorPipelineResolver.ResolvePipeline(actor.GetType());
            pipeline.AfterActorIncarnated(actor, this);
            
            var initializableActor = actor as IInitializableActor;
            if(initializableActor != null)
            {
                initializableActor.Init();
            }
            return actor;
        }

        public void UseThreadContext(Action action)
        {
            var tmp = InternalCurrentActorCellKeeper.Current;
            InternalCurrentActorCellKeeper.Current = this;
            try
            {
                action();
            }
            finally
            {
                //ensure we set back the old context
                InternalCurrentActorCellKeeper.Current = tmp;
            }
        }


        public virtual void Post(IActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this._systemImpl.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (_systemImpl.Settings.SerializeAllMessages && !(message is INoSerializationVerificationNeeded))
            {
                Serializer serializer = _systemImpl.Serialization.FindSerializerFor(message);
                byte[] serialized = serializer.ToBinary(message);
                object deserialized = _systemImpl.Serialization.Deserialize(serialized, serializer.Identifier,
                    message.GetType());
                message = deserialized;
            }           

            var m = new Envelope
            {
                Sender = sender,
                Message = message,
            };

            Mailbox.Post(Self, m);
        }

        protected void ClearActorCell()
        {
            //TODO_ UnstashAll stashed system messages (this is not the same stash that might exist on the actor)
            _props = terminatedProps;
        }

        protected void ClearActor(ActorBase actor)
        {
            if (actor != null)
            {
                var disposable = actor as IDisposable;
                if (disposable != null)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception e)
                    {
                        if (_systemImpl.Log != null)
                        {
                            _systemImpl.Log.Error(e, "An error occurred while disposing {0} actor. Reason: {1}", 
                                actor.GetType(), e.Message);
                        }
                    }
                }

                ReleaseActor(actor);
                actor.Clear(_systemImpl.DeadLetters);
            }
            _actorHasBeenCleared = true;
            CurrentMessage = null;

            //TODO: semantics here? should all "_state" be cleared? or just behavior?
            _state = _state.ClearBehaviorStack();
        }

        private void ReleaseActor(ActorBase a)
        {
            _props.Release(a);
        }

        protected void PrepareForNewActor()
        {
            _state = _state.ClearBehaviorStack();
            _actorHasBeenCleared = false;
        }
        protected void SetActorFields(ActorBase actor)
        {
            if (actor != null)
            {
                actor.Unclear();
            }
        }
        public static NameAndUid SplitNameAndUid(string name)
        {
            var i = name.IndexOf('#');
            return i < 0 
                ? new NameAndUid(name, UndefinedUid)
                : new NameAndUid(name.Substring(0, i), Int32.Parse(name.Substring(i + 1)));
        }

        public static IActorRef GetCurrentSelfOrNoSender()
        {
            var current = Current;
            return current != null ? current.Self : ActorRefs.NoSender;
        }

        public static IActorRef GetCurrentSenderOrNoSender()
        {
            var current = Current;
            return current != null ? current.Sender : ActorRefs.NoSender;
        }
    }
}

