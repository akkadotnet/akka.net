using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Actor
{
    public partial class ActorCell : IActorContext, IUntypedActorContext, Cell 
    {
        /// <summary>NOTE! Only constructor and ClearActorFields is allowed to update this</summary>
        private InternalActorRef _self;
        public const int UndefinedUid = 0;
        private Props _props;
        private static Props _terminatedProps=new TerminatedProps();
        [ThreadStatic] private static ActorCell current;

        protected ConcurrentDictionary<string, InternalActorRef> children =
            new ConcurrentDictionary<string, InternalActorRef>();

        protected HashSet<ActorRef> watchees = new HashSet<ActorRef>();
        protected Stack<Receive> behaviorStack = new Stack<Receive>();
        private long uid;
        private ActorBase _actor;
        private bool _actorHasBeenCleared;


        public ActorCell(ActorSystem system, InternalActorRef self, Props props, MessageDispatcher dispatcher, InternalActorRef parent)
        {
            _self = self;
            _props = props;
            System = system;
            Parent = parent;
            Dispatcher = dispatcher;            
        }

        public object CurrentMessage { get; private set; }
        public Mailbox Mailbox { get; protected set; }
        public MessageDispatcher Dispatcher { get; private set; }
        public bool IsLocal { get{return true;} }

        internal static ActorCell Current
        {
            get { return current; }
        }

        public ActorSystem System { get; private set; }
        public Props Props { get { return _props; } }
        public ActorRef Self { get { return _self; } }
        ActorRef IActorContext.Parent { get { return Parent; } }
        public InternalActorRef Parent { get; private set; }
        public ActorRef Sender { get; private set; }
        public bool HasMessages { get { return Mailbox.HasUnscheduledMessages; } }
        public int NumberOfMessages { get { return Mailbox.NumberOfMessages; } }
        internal bool ActorHasBeenCleared { get { return _actorHasBeenCleared; } }

        public void Init(bool sendSupervise, Func<Mailbox> createMailbox /*, MailboxType mailboxType*/) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType
        {
            var mailbox = createMailbox(); //Akka: dispatcher.createMailbox(this, mailboxType)
            mailbox.Setup(Dispatcher);
            mailbox.SetActor(this);
            Mailbox = mailbox;

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
            mailbox.Post(new Envelope(){Message = createMessage, Sender = Self});

            if(sendSupervise)
            {
                Parent.Tell(new Supervise(Self, async: false));
            }
        }

        public InternalActorRef GetChildByName(string name)   //TODO: Should return  Option[ChildStats]
        {
            return GetSingleChild(name);
        }

        public InternalActorRef GetSingleChild(string name)
        {
            InternalActorRef actorRef;
            children.TryGetValue(name, out actorRef);
            if(actorRef.IsNobody())
                return ActorRef.Nobody;
            return actorRef;
        }

        ActorRef IActorContext.Child(string name)
        {
            return GetSingleChild(name);
        }

        public ActorSelection ActorSelection(string path)
        {
            return ActorRefFactoryShared.ActorSelection(path, System, Self);
        }

        public ActorSelection ActorSelection(ActorPath path)
        {
            return ActorRefFactoryShared.ActorSelection(path, System);
        }

        public virtual InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return ActorOf(Props.Create<TActor>(), name);
        }

        public virtual InternalActorRef ActorOf(Props props, string name = null)
        {
            return MakeChild(props, name);
        }


        IEnumerable<ActorRef> IActorContext.GetChildren()
        {
            return GetChildren();
        }

        public IEnumerable<InternalActorRef> GetChildren()
        {
            return children.Values.ToList();
        }


        public void Become(Receive receive, bool discardOld = true)
        {
            if(discardOld && behaviorStack.Count > 1) //We should never pop off the initial receiver
                behaviorStack.Pop();
            behaviorStack.Push(receive);
        }

        public void Unbecome()
        {
            if(behaviorStack.Count>1)   //We should never pop off the initial receiver
                behaviorStack.Pop();
        }

        void IUntypedActorContext.Become(UntypedReceive receive, bool discardOld)
        {
            Become(m => { receive(m); return true; }, discardOld);
        }

        /// <summary>
        ///     May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Watch(ActorRef watchee)
        {
            watchees.Add(watchee);
            watchee.Tell(new Watch(watchee, Self),Self);
            //If watchee is terminated, its mailbox have been replaced by 
            //DeadLetterMailbox, which will forward the Watch message as a
            //DeadLetter to DeadLetterActorRef. It inspects the message inside
            //the DeadLetter, sees it is a Watch and sends watcher, i.e. us
            //a DeathWatchNotification(watchee)
        }

        /// <summary>
        ///     May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Unwatch(ActorRef watchee)
        {
            watchees.Remove(watchee);
            watchee.Tell(new Unwatch(watchee, Self));
        }

        private InternalActorRef MakeChild(Props props, string name)
        {
            long childUid = NewUid();
            name = GetActorName(name, childUid);
            //reserve the name before we create the actor
            ReserveChild(name);
            InternalActorRef actor;
            try
            {
                ActorPath childPath = (Self.Path/name).WithUid(childUid);
                actor = System.Provider.ActorOf(System, props, _self, childPath,false,null,true,false);
            }
            catch
            {
                //if actor creation failed, unreserve the name
                UnreserveChild(name);
                throw;
            }
            //replace the reservation with the real actor
            InitChild(actor);
            actor.Start();
            return actor;
        }

        private void UnreserveChild(string name)
        {
            InternalActorRef tmp;
            children.TryRemove(name, out tmp);
        }

        /// <summary>This should only be used privately or when creating the root actor. </summary>
        public void InitChild(InternalActorRef actor)
        {
            children.TryUpdate(actor.Path.Name, actor, ActorRef.Reserved);
        }

        public void ReserveChild(string name)
        {
            if (!children.TryAdd(name, ActorRef.Reserved))
            {
                throw new Exception("The name is already reserved: " + name);
            }
        }

        private long NewUid()
        {
            long auid = Interlocked.Increment(ref uid);
            return auid;
        }

        private string GetActorName(string name, long actorUid)
        {
            return name ?? ("$" + actorUid.Base64Encode());
        }

        private ActorBase NewActor()
        {
            ActorBase instance=null;
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                behaviorStack.Clear();
                instance = CreateNewActorInstance();
                instance.supervisorStrategy = _props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantion unless explicitly set in props
            });
            return instance;
        }

        protected virtual ActorBase CreateNewActorInstance()
        {
            return _props.NewActor();
        }

        public void UseThreadContext(Action action)
        {
            ActorCell tmp = Current;
            current = this;
            try
            {
                action();
            }
            finally
            {
                //ensure we set back the old context
                current = tmp;
            }
        }


        public virtual void Post(ActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this.System.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (System.Settings.SerializeAllMessages && !(message is NoSerializationVerificationNeeded))
            {
                Serializer serializer = System.Serialization.FindSerializerFor(message);
                byte[] serialized = serializer.ToBinary(message);
                object deserialized = System.Serialization.Deserialize(serialized, serializer.Identifier,
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

            Mailbox.Post(m);
        }

        protected void ClearActorCell()
        {
            //TODO: UnstashAll();
            _props = _terminatedProps;
        }

        protected void ClearActor()
        {
            if(_actor != null)
            {
                _actor.Clear(System.DeadLetters);
            }
            _actorHasBeenCleared = true;
            CurrentMessage = null;
            behaviorStack = null;
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
    }
}