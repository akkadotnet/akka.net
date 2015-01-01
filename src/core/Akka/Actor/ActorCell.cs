using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public partial class ActorCell : IActorContext, IUntypedActorContext, Cell 
    {
        /// <summary>NOTE! Only constructor and ClearActorFields is allowed to update this</summary>
        private InternalActorRef _self;
        public const int UndefinedUid = 0;
        private Props _props;
        private static readonly Props terminatedProps=new TerminatedProps();

        protected Stack<Receive> behaviorStack = new Stack<Receive>();
        private long _uid;
        private ActorBase _actor;
        private bool _actorHasBeenCleared;
        private Mailbox _mailbox;
        private readonly ActorSystemImpl _systemImpl;


        public ActorCell(ActorSystemImpl system, InternalActorRef self, Props props, MessageDispatcher dispatcher, InternalActorRef parent)
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
        public ActorRef Self { get { return _self; } }
        ActorRef IActorContext.Parent { get { return Parent; } }
        public InternalActorRef Parent { get; private set; }
        public ActorRef Sender { get; private set; }
        public bool HasMessages { get { return Mailbox.HasUnscheduledMessages; } }
        public int NumberOfMessages { get { return Mailbox.NumberOfMessages; } }
        internal bool ActorHasBeenCleared { get { return _actorHasBeenCleared; } }
        internal static Props TerminatedProps { get { return terminatedProps; } }

        public void Init(bool sendSupervise, Func<Mailbox> createMailbox /*, MailboxType mailboxType*/) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType
        {
            var mailbox = createMailbox(); //Akka: dispatcher.createMailbox(this, mailboxType)
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
        public InternalActorRef GetChildByName(string name)   //TODO: Should return  Option[ChildStats]
        {
            InternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRef.Nobody;
        }

        ActorRef IActorContext.Child(string name)
        {
            InternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRef.Nobody;
        }

        public ActorSelection ActorSelection(string path)
        {
            return ActorRefFactoryShared.ActorSelection(path, _systemImpl, Self);
        }

        public ActorSelection ActorSelection(ActorPath path)
        {
            return ActorRefFactoryShared.ActorSelection(path, _systemImpl);
        }


        IEnumerable<ActorRef> IActorContext.GetChildren()
        {
            return GetChildren();
        }

        public IEnumerable<InternalActorRef> GetChildren()
        {
            return ChildrenContainer.Children;
        }


        public void Become(Receive receive, bool discardOld = true)
        {
            if(discardOld && behaviorStack.Count > 1) //We should never pop off the initial receiver
                behaviorStack.Pop();
            behaviorStack.Push(receive);
        }

        public void Unbecome()
        {
            if (behaviorStack.Count > 1) //We should never pop off the initial receiver
                behaviorStack.Pop();                
        }
  
        void IUntypedActorContext.Become(UntypedReceive receive, bool discardOld)
        {
            Become(m => { receive(m); return true; }, discardOld);
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
                behaviorStack = new Stack<Receive>();
                instance = CreateNewActorInstance();
                instance.SupervisorStrategyInternal = _props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantiation unless explicitly set in props
            });
            return instance;
        }

        protected virtual ActorBase CreateNewActorInstance()
        {
            var actor = _props.NewActor();

            // Apply default of custom behaviors to actor.
            _systemImpl.ActorProducerPipeline.AfterActorCreated(actor, this);
            
            var initializableActor = actor as InitializableActor;
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


        public virtual void Post(ActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this._systemImpl.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (_systemImpl.Settings.SerializeAllMessages && !(message is NoSerializationVerificationNeeded))
            {
                Serializer serializer = _systemImpl.Serialization.FindSerializerFor(message);
                byte[] serialized = serializer.ToBinary(message);
                object deserialized = _systemImpl.Serialization.Deserialize(serialized, serializer.Identifier,
                    message.GetType());
                message = deserialized;
            }
            
            //Execute CompleteFuture objects inline - if the Actor is waiting on the result of an Ask operation inside
            //its receive method, then the mailbox will never schedule the CompleteFuture.
            //Thus - we execute it inline, outside of the mailbox.
            if (message is CompleteFuture)
            {
                HandleCompleteFuture(message.AsInstanceOf<CompleteFuture>());
                return;
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

                actor.Clear(_systemImpl.DeadLetters);
            }
            _actorHasBeenCleared = true;
            CurrentMessage = null;
            behaviorStack = null;
        }

        protected void PrepareForNewActor()
        {
            behaviorStack = new Stack<Receive>();
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

        public static ActorRef GetCurrentSelfOrNoSender()
        {
            var current = Current;
            return current != null ? current.Self : NoSender.Instance;
        }

        public static ActorRef GetCurrentSenderOrNoSender()
        {
            var current = Current;
            return current != null ? current.Sender : NoSender.Instance;
        }
    }
}