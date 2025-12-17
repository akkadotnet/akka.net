//-----------------------------------------------------------------------
// <copyright file="ActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Serialization;
using Akka.Util;
using Assert = System.Diagnostics.Debug;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The hosting infrastructure for actors.
    /// </summary>
    public partial class ActorCell : IUntypedActorContext, ICell
    {
        /// <summary>NOTE! Only constructor and ClearActorFields is allowed to update this</summary>
        private readonly IInternalActorRef _self;

        /// <summary>
        /// Constant placeholder value for actors without a defined unique identifier.
        /// </summary>
        public const int UndefinedUid = 0;

        private const int DefaultState = 0;
        private const int SuspendedState = 1;
        private const int SuspendedWaitForChildrenState = 2;

        private volatile Mailbox? _mailboxDoNotCallMeDirectly;
        private ActorTaskScheduler? _taskScheduler;

        // special system message stash, used when we aren't able to handle other system messages just yet
        private LatestFirstSystemMessageList _sysMsgStash = SystemMessageList.LNil;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="msg">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="parent">TBD</param>
        public ActorCell(ActorSystemImpl system, IInternalActorRef self, Props props, MessageDispatcher dispatcher, IInternalActorRef parent)
        {
            _self = self;
            Props = props;
            SystemImpl = system;
            Parent = parent;
            Dispatcher = dispatcher;
        }

        /// <summary>
        /// The current message the actor is processing right now.
        /// </summary>
        /// <remarks>
        /// Will be set to <c>null</c> when not processing any message.
        /// </remarks>
        public object? CurrentMessage { get; internal set; }
        
        /// <summary>
        /// This actor's mailbox instance.
        /// </summary>
        public Mailbox? Mailbox => _mailboxDoNotCallMeDirectly;

        /// <summary>
        /// This actor's message dispatcher.
        /// </summary>
        public MessageDispatcher Dispatcher { get; private set; }
        
        /// <summary>
        /// Returns <c>true</c> if this actor is local to the system. Can only return <c>false</c> if this actor
        /// is remotely deployed from elsewhere.
        /// </summary>
        public bool IsLocal { get { return true; } }
        
        /// <summary>
        /// A reference to the current actor instance
        /// </summary>
        internal ActorBase? Actor { get; private set; }

        /// <summary>
        /// Indicates whether the actor is currently terminated.
        /// </summary>
        public bool IsTerminated
        {
            get
            {
                Assert.Assert(Mailbox != null, $"{nameof(Mailbox)} should never be null when {nameof(IsTerminated)} property is accessed");
                return Mailbox!.IsClosed();
            }
        }
        
        /// <summary>
        /// A static reference to the current actor cell.
        /// </summary>
        internal static ActorCell? Current
        {
            get { return InternalCurrentActorCellKeeper.Current; }
        }

        /// <summary>
        /// The <see cref="ActorSystem"/> this actor belongs to.
        /// </summary>
        public ActorSystem System { get { return SystemImpl; } }
        
        /// <summary>
        /// INTERNAL API
        /// </summary>
        public ActorSystemImpl SystemImpl { get; }

        /// <summary>
        /// The <see cref="Props"/> used to create this actor.
        /// </summary>
        /// <remarks>
        /// Will be re-used in the event that the actor restarts.
        /// </remarks>
        public Props Props { get; private set; }

        /// <summary>
        /// The <see cref="IActorRef"/> instance that represents the current actor.
        /// </summary>
        public IActorRef Self { get { return _self; } }
        IActorRef IActorContext.Parent { get { return Parent; } }
        
        /// <summary>
        /// This actor's parent actor.
        /// </summary>
        public IInternalActorRef Parent { get; private set; }
        
        /// <summary>
        /// The actor who sent us <see cref="CurrentMessage"/>.
        /// </summary>
        /// <remarks>
        /// Will be <c>null</c> when we are not processing messages.
        /// </remarks>
        public IActorRef? Sender { get; private set; }
        
        /// <summary>
        /// Will return <c>true</c> if <see cref="NumberOfMessages"/> is greater than zero.
        /// </summary>
        public bool HasMessages {
            get
            {
                Assert.Assert(Mailbox != null, $"{nameof(Mailbox)} should never be null when {nameof(HasMessages)} property is accessed");
                return Mailbox!.HasMessages;
            }
        }
        
        /// <summary>
        /// Current message count inside the mailbox.
        /// </summary>
        public int NumberOfMessages {
            get
            {
                Assert.Assert(Mailbox != null, $"{nameof(Mailbox)} should never be null when {nameof(NumberOfMessages)} property is accessed");
                return Mailbox!.NumberOfMessages;
            } 
        }
        
        /// <summary>
        /// Indicates if we've been cleared after a restart.
        /// </summary>
        internal bool ActorHasBeenCleared { get; private set; }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal static Props TerminatedProps { get; } = new TerminatedProps();

        /// <summary>
        /// Used by this actor to schedule <see cref="Task"/> instances.
        /// </summary>
        public virtual ActorTaskScheduler TaskScheduler
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
        /// <param name="sendSupervise">TBD</param>
        /// <param name="mailboxType">TBD</param>
        public void Init(bool sendSupervise, MailboxType mailboxType)
        {
            /*
             * Create the mailbox and enqueue the Create() message to ensure that
             * this is processed before anything else.
             */
            var mailbox = MessageDispatcher.CreateMailbox(this, mailboxType);

            Create createMessage;
            /*
             * The mailboxType was calculated taking into account what the MailboxType
             * has promised to produce. If that was more than the default, then we need
             * to re-verify here because the dispatcher may well have screwed it up.
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
                    createMessage = new Create(new ActorInitializationException(Self, $"Actor [{Self}] requires mailbox type [{req}] got [{gotType}]"));
                }
            }
            else
            {
                createMessage = new Create(null);
            }

            SwapMailbox(mailbox);
            Assert.Assert(Mailbox != null, $"{nameof(Mailbox)} should never be null after {nameof(SwapMailbox)} has been invoked");
            Mailbox!.SetActor(this);

            //// ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
            var self = Self;
            mailbox.SystemEnqueue(self, createMessage);

            if (sendSupervise)
            {
                Parent.SendSystemMessage(new Supervise(self, async: false));
            }
        }

        /// <summary>
        /// Obsolete. Use <see cref="TryGetChildStatsByName(string, out IChildStats)"/> instead.
        /// </summary>
        /// <param name="name">N/A</param>
        /// <returns>N/A</returns>
        [Obsolete("Use TryGetChildStatsByName [0.7.1]", true)]
        public IInternalActorRef GetChildByName(string name)   //TODO: Should return  Option[ChildStats]
        {
            return GetSingleChild(name);
        }

        IActorRef IActorContext.Child(string name)
        {
            if (TryGetChildStatsByName(name, out var child) && child is ChildRestartStats s)
                return s.Child;
            
            return ActorRefs.Nobody;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public ActorSelection ActorSelection(string path)
        {
            return ActorRefFactoryShared.ActorSelection(path, SystemImpl, Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public ActorSelection ActorSelection(ActorPath path)
        {
            return ActorRefFactoryShared.ActorSelection(path, SystemImpl);
        }


        IEnumerable<IActorRef> IActorContext.GetChildren()
        {
            return GetChildren();
        }

        /// <summary>
        /// Returns all the children of this actor.
        /// </summary>
        public IEnumerable<IInternalActorRef> GetChildren()
        {
            return ChildrenContainer.Children;
        }

        /// <summary>
        /// Changes behaviors to the new <see cref="Receive"/>.
        /// </summary>
        /// <remarks>
        /// Forgets any prior behaviors and doesn't maintain a "behavior stack."
        ///
        /// See <see cref="BecomeStacked"/> if that's what you want.
        /// </remarks>
        public void Become(Receive receive)
        {
            _state = _state.Become(receive);
        }

        /// <summary>
        /// Changes behaviors to the new <see cref="Receive"/> while maintaining the previous
        /// behavior in this actor's "behavior stack."
        /// </summary>
        /// <remarks>
        /// Most users typically use <see cref="Become"/> so they can avoid having to maintain a behavior stack.
        ///
        /// Only use this method if you know what you're doing.
        /// </remarks>
        public void BecomeStacked(Receive receive)
        {
            _state = _state.BecomeStacked(receive);
        }

        /// <summary>
        /// Used in combination with <see cref="BecomeStacked"/> - used to pop old behaviors off of the stack.
        /// </summary>
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

        private static long NewUid()
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
            ActorBase? instance = null;
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                _state = _state.ClearBehaviorStack();
                instance = CreateNewActorInstance();
                //TODO: this overwrites any already initialized supervisor strategy
                //We should investigate what we can do to handle this better
                instance.SupervisorStrategyInternal = Props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantiation unless explicitly set in props
            });


            Assert.Assert(instance != null, $"{nameof(instance)} should never be null at this point");
            return instance!;
        }

        /// <summary>
        /// Used to create a new instance of the actor from its <see cref="Props"/>
        /// </summary>
        /// <returns>A new instance of this actor.</returns>
        protected virtual ActorBase CreateNewActorInstance()
        {
            var actor = Props.NewActor();

            // Apply default of custom behaviors to actor.
            var pipeline = SystemImpl.ActorPipelineResolver.ResolvePipeline(actor.GetType());

            pipeline.AfterActorIncarnated(actor, this);

            if (actor is IInitializableActor initializableActor)
            {
                initializableActor.Init();
            }
            return actor;
        }

        /// <summary>
        /// Performs an activity inside this actor's thread-safe context.
        /// </summary>
        /// <param name="action">The action to perform.</param>
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

        /// <summary>
        /// Used to send a message to the actor's mailbox.
        /// </summary>
        /// <param name="message">The message + sender envelope.</param>
        public virtual void SendMessage(Envelope message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this._systemImpl.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            try
            {
                var messageToDispatch = SystemImpl.Settings.SerializeAllMessages
                    ? SerializeAndDeserialize(message)
                    : message;

                Dispatcher.Dispatch(this, messageToDispatch);
            }
            catch (Exception e)
            {
                SystemImpl.EventStream.Publish(new Error(e, _self.Parent.ToString(), ActorType, "Swallowing exception during message send"));
            }
        }

        /// <summary>
        /// Used to send a message to the actor's mailbox.
        /// </summary>
        /// <param name="sender">The sender of the message.</param>
        /// <param name="message">The message to send.</param>
        public virtual void SendMessage(IActorRef sender, object message)
        {
            SendMessage(new Envelope(message, sender, System));
        }

        /// <summary>
        /// Clears all the contents of the ActorCell.
        /// </summary>
        /// <remarks>
        /// Used during shutdown.
        /// </remarks>
        protected void ClearActorCell()
        {
            UnstashAll();
            Props = TerminatedProps;
        }

        /// <summary>
        /// Clears the actor's state and prepares it for GC.
        /// </summary>
        /// <param name="actor">The actor instance.</param>
        /// <remarks>
        /// Used during actor restarts and shutdowns.
        /// </remarks>
        protected void ClearActor(ActorBase? actor)
        {
            if (actor != null)
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (actor is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception e)
                    {
                        SystemImpl.Log?.Error(e, "An error occurred while disposing {0} actor. Reason: {1}",
                            actor.GetType(), e.Message);
                    }
                }

                ReleaseActor(actor);
                actor.Clear(SystemImpl.DeadLetters);
            }
            ActorHasBeenCleared = true;
            CurrentMessage = null;
            
            _state = _state.ClearBehaviorStack();
        }

        private void ReleaseActor(ActorBase a)
        {
            Props.Release(a);
        }

        /// <summary>
        /// Clears the state for a new actor instance following a restart.
        /// </summary>
        protected void PrepareForNewActor()
        {
            _state = _state.ClearBehaviorStack();
            ActorHasBeenCleared = false;
        }

        protected static void SetActorFields(ActorBase? actor)
        {
            actor?.Unclear();
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="name">The full name of the actor, including the UID if known</param>
        /// <returns>A new (string name, int uid) instance.</returns>
        internal static (string name, int uid) GetNameAndUid(string name)
        {
            var i = name.IndexOf('#');
            return i < 0
                ? (name, UndefinedUid)
                : (name.Substring(0, i), SpanHacks.Parse(name.AsSpan(i + 1)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static IActorRef? GetCurrentSelfOrNoSender()
        {
            var current = Current;
            return current != null ? current.Self : ActorRefs.NoSender;
        }

        /// <summary>
        /// Gets the current sender or <see cref="ActorRefs.NoSender"/>
        /// </summary>
        public static IActorRef? GetCurrentSenderOrNoSender()
        {
            var current = Current;
            return current?.Sender ?? ActorRefs.NoSender;
        }

        #nullable enable
        private Envelope SerializeAndDeserialize(Envelope envelope)
        {
            // in case someone has an IWrappedMessage that ALSO implements INoSerializationVerificationNeeded
            if(envelope.Message is INoSerializationVerificationNeeded)
                return envelope;
            
            // recursively unwraps message, no need to check for DeadLetter because DeadLetter inherits IWrappedMessage
            var unwrapped = WrappedMessage.Unwrap(envelope.Message);
            
            // don't do serialization verification if the underlying type doesn't require it
            if (unwrapped is INoSerializationVerificationNeeded)
                return envelope;

            object deserializedMsg;
            try
            {
                deserializedMsg = SerializeAndDeserializePayload(unwrapped);
            }
            catch (Exception e)
            {
                Assert.Assert(Actor != null, $"{nameof(Actor)} should never be null at this point");
                throw new SerializationException($"Failed to serialize and deserialize payload object [{unwrapped.GetType()}]. Envelope: [{envelope}], Actor type: [{Actor!.GetType()}]", e);
            }

            // Check that this message was ever wrapped
            if (ReferenceEquals(envelope.Message, unwrapped))
                return new Envelope(deserializedMsg, envelope.Sender);

            // In the case above this one, we're able to deserialize the message, and we returned that
            // (a new copy of the message), however, when we're dealing with wrapped messages, it's impossible to
            // reconstruct the russian dolls together again, so we just return the original envelope.
            // We guarantee that we can de/serialize in innermost message, but we can not guarantee to return
            // a new copy.
            return envelope;
        }
        #nullable restore

        private object SerializeAndDeserializePayload(object obj)
        {
            var serializer = SystemImpl.Serialization.FindSerializerFor(obj);
            var oldInfo = Serialization.Serialization.CurrentTransportInformation;
            try
            {
                if (oldInfo == null)
                    Serialization.Serialization.CurrentTransportInformation =
                        SystemImpl.Provider.SerializationInformation;

                var bytes = serializer.ToBinary(obj);

                var manifest = Serialization.Serialization.ManifestFor(serializer, obj);
                return SystemImpl.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
            }
            finally
            {
                Serialization.Serialization.CurrentTransportInformation = oldInfo;
            }
        }
    }
}

