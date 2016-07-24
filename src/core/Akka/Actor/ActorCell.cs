//-----------------------------------------------------------------------
// <copyright file="ActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Serialization;
using Akka.Util;
using Assert = System.Diagnostics.Debug;

namespace Akka.Actor
{
    public partial class ActorCell : IUntypedActorContext, ICell 
    {
        /// <summary>NOTE! Only constructor and ClearActorFields is allowed to update this</summary>
        private IInternalActorRef _self;
        public const int UndefinedUid = 0;
        private Props _props;
        private static readonly Props terminatedProps =new TerminatedProps();

        private const int DefaultState = 0;
        private const int SuspendedState = 1;
        private const int SuspendedWaitForChildrenState = 2;
        // todo: might need a special state for AsyncAwait

        private ActorBase _actor;
        private bool _actorHasBeenCleared;
        private volatile Mailbox _mailboxDoNotCallMeDirectly;
        private readonly ActorSystemImpl _systemImpl;
        private ActorTaskScheduler _taskScheduler;

        // special system message stash, used when we aren't able to handle other system messages just yet
        private LatestFirstSystemMessageList _sysMsgStash = SystemMessageList.LNil;

        protected void Stash(SystemMessage msg)
        {
            Assert.Assert(msg.Unlinked);
            _sysMsgStash = _sysMsgStash + msg;
        }

        private LatestFirstSystemMessageList UnstashAll()
        {
            var unstashed = _sysMsgStash;
            _sysMsgStash = SystemMessageList.LNil;
            return unstashed;
        }


        public ActorCell(ActorSystemImpl system, IInternalActorRef self, Props props, MessageDispatcher dispatcher, IInternalActorRef parent)
        {
            _self = self;
            _props = props;
            _systemImpl = system;
            Parent = parent;
            Dispatcher = dispatcher;
            
        }

        public object CurrentMessage { get; private set; }
        public Mailbox Mailbox => Volatile.Read(ref _mailboxDoNotCallMeDirectly);

        public MessageDispatcher Dispatcher { get; private set; }
        public bool IsLocal { get{return true;} }
        protected ActorBase Actor { get { return _actor; } }
        public bool IsTerminated => Mailbox.IsClosed();
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
        public bool HasMessages { get { return Mailbox.HasMessages; } }
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

        /// <summary>
        /// Initialize this cell, i.e. set up mailboxes and supervision. The UID must be
        /// reasonably different from the previous UID of a possible actor with the same path,
        /// which can be achieved by using <see cref="ThreadLocalRandom"/>
        /// </summary>
        /// <param name="sendSupervise"></param>
        /// <param name="mailboxType"></param>
        public void Init(bool sendSupervise, MailboxType mailboxType)
        {
            /*
             * Create the mailbox and enqueue the Create() message to ensure that
             * this is processed before anything else.
             */
            var mailbox = Dispatcher.CreateMailbox(this, mailboxType);

            Create createMessage;
            /*
             * The mailboxType was calculated taking into account what the MailboxType
             * has promised to produce. If that was more than the default, then we need
             * to reverify here because the dispatcher may well have screwed it up.
             */
            // we need to delay the failure to the point of actor creation so we can handle
            // it properly in the normal way
            var actorClass = Props.Type;
            if (System.Mailboxes.ProducesMessageQueue(mailboxType.GetType()) && System.Mailboxes.HasRequiredType(actorClass))
            {
                var req = System.Mailboxes.GetRequiredType(actorClass);
                if (req.IsInstanceOfType(mailbox.MessageQueue)) createMessage = new Create(null); //success
                else
                {
                    var gotType = mailbox.MessageQueue == null ? "null" : mailbox.MessageQueue.GetType().FullName;
                    createMessage = new Create(new ActorInitializationException(Self,$"Actor [{Self}] requires mailbox type [{req}] got [{gotType}]"));
                }
            }
            else
            {
               createMessage = new Create(null);
            }

            SwapMailbox(mailbox);
            Mailbox.SetActor(this);

            //// ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
            var self = Self;
            mailbox.SystemEnqueue(self, createMessage);

            if(sendSupervise)
            {
                Parent.SendSystemMessage(new Supervise(self, async: false));
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
            // Note that this uid is also used as hashCode in ActorRef, so be careful
            // to not break hashing if you change the way uid is generated
            var uid = ThreadLocalRandom.Current.Next();
            while (uid == UndefinedUid)
                uid = ThreadLocalRandom.Current.Next();
            return uid;
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

        public virtual void SendMessage(Envelope message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this._systemImpl.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (_systemImpl.Settings.SerializeAllMessages)
            {
                DeadLetter deadLetter;
                var unwrapped = (deadLetter = message.Message as DeadLetter) != null ? deadLetter.Message : message.Message;
                if (!(unwrapped is INoSerializationVerificationNeeded))
                {
                    Serializer serializer = _systemImpl.Serialization.FindSerializerFor(message.Message);
                    byte[] serialized = serializer.ToBinary(message.Message);

                    var manifestSerializer = serializer as SerializerWithStringManifest;
                    if (manifestSerializer != null)
                    {
                        var manifest = manifestSerializer.Manifest(serialized);
                        message = new Envelope(_systemImpl.Serialization.Deserialize(serialized, manifestSerializer.Identifier, manifest), message.Sender);
                    }
                    else
                    {
                        message = new Envelope(_systemImpl.Serialization.Deserialize(serialized, serializer.Identifier, message.Message.GetType().AssemblyQualifiedName), message.Sender);
                    }
                }
            }

            Dispatcher.Dispatch(this, message);
        }

        public virtual void SendMessage(IActorRef sender, object message)
        {
            SendMessage(new Envelope(message, sender, System));
        }

        protected void ClearActorCell()
        {
            UnstashAll();
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

