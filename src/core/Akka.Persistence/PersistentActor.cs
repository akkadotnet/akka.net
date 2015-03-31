using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Tools.MatchHandler;

namespace Akka.Persistence
{
    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> if a journal fails to write a persistent message. 
    /// If not handled, an <see cref="ActorKilledException"/> is thrown by that persistent actor.
    /// </summary>
    [Serializable]
    public sealed class PersistenceFailure
    {
        public PersistenceFailure(object payload, long sequenceNr, Exception cause)
        {
            Payload = payload;
            SequenceNr = sequenceNr;
            Cause = cause;
        }

        /// <summary>
        /// Payload of the persistent message.
        /// </summary>
        public object Payload { get; private set; }
        
        /// <summary>
        /// Sequence number of the persistent message.
        /// </summary>
        public long SequenceNr { get; private set; }

        /// <summary>
        /// Failure cause.
        /// </summary>
        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> if a journal fails to replay messages or fetch that 
    /// persistent actor's highest sequence number. If not handled, the actor will be stopped.
    /// </summary>
    [Serializable]
    public sealed class RecoveryFailure
    {
        public RecoveryFailure(Exception cause)
        {
            Cause = cause;
            SequenceNr = -1;
        }

        public RecoveryFailure(Exception cause, long sequenceNr, object payload)
        {
            Cause = cause;
            SequenceNr = sequenceNr;
            Payload = payload;
        }

        public Exception Cause { get; private set; }
        public long SequenceNr { get; set; }
        public object Payload { get; set; }
    }

    [Serializable]
    public sealed class RecoveryCompleted
    {
        public static readonly RecoveryCompleted Instance = new RecoveryCompleted();
        private RecoveryCompleted(){}

        public override bool Equals(object obj)
        {
            return obj is RecoveryCompleted;
        }
    }

    /// <summary>
    /// Instructs a <see cref="PersistentActor"/> to recover itself. Recovery will start from the first previously saved snapshot
    /// matching provided <see cref="FromSnapshot"/> selection criteria, if any. Otherwise it will replay all journaled messages.
    /// 
    /// If recovery starts from a snapshot, the <see cref="PersistentActor"/> is offered with that snapshot wrapped in 
    /// <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger than the snapshot, up to the
    /// specified upper sequence number bound (<see cref="ToSequenceNr"/>).
    /// </summary>
    [Serializable]
    public sealed class Recover
    {
        public static readonly Recover Default = new Recover(SnapshotSelectionCriteria.Latest);
        public Recover(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue)
        {
            FromSnapshot = fromSnapshot;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// Criteria for selecting a saved snapshot from which recovery should start. Default is del youngest snapshot.
        /// </summary>
        public SnapshotSelectionCriteria FromSnapshot { get; private set; }

        /// <summary>
        /// Upper, inclusive sequence number bound. Default is no upper bound.
        /// </summary>
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// Maximum number of messages to replay. Default is no limit.
        /// </summary>
        public long ReplayMax { get; private set; }
    }

    /// <summary>
    /// Persistent actor - can be used to implement command or eventsourcing.
    /// </summary>
    public abstract class PersistentActor : Eventsourced
    {
        protected override bool Receive(object message)
        {
            return ReceiveCommand(message);
        }
    }
    
    /// <summary>
    /// Persistent actor - can be used to implement command or eventsourcing.
    /// </summary>
    public abstract class UntypedPersistentActor : Eventsourced
    {
        protected override bool Receive(object message)
        {
            return ReceiveCommand(message);
        }

        protected sealed override bool ReceiveCommand(object message)
        {
            OnCommand(message);
            return true;
        }

        protected sealed override bool ReceiveRecover(object message)
        {
            OnRecover(message);
            return true;
        }

        protected abstract void OnCommand(object message);
        protected abstract void OnRecover(object message);

        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        protected void Become(UntypedReceive receive, bool discardOld = true)
        {
            if (discardOld)
                Context.Become(receive);
            else
                Context.BecomeStacked(receive);
        }


        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void Become(UntypedReceive receive)
        {
            Context.Become(receive);
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IUntypedActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IUntypedActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void BecomeStacked(UntypedReceive receive)
        {
            Context.BecomeStacked(receive);
        }


        protected static new IUntypedActorContext Context { get { return (IUntypedActorContext)ActorBase.Context; } }
    }

    public abstract class ReceivePersistentActor : UntypedPersistentActor, InitializableActor
    {
        
        private bool _shouldUnhandle = true;
        private readonly Stack<MatchBuilder> _matchCommandBuilders = new Stack<MatchBuilder>();
        private readonly Stack<MatchBuilder> _matchRecoverBuilders = new Stack<MatchBuilder>();
        private PartialAction<object> _partialReceiveCommand = _ => false;
        private PartialAction<object> _partialReceiveRecover = _ => false;
        private bool _hasBeenInitialized;

        protected ReceivePersistentActor()
        {
            PrepareConfigureMessageHandlers();
        }

        void InitializableActor.Init()
        {
            //This might be called directly after the constructor, or when the same actor instance has been returned
            //during recreate. Make sure what happens here is idempotent
            if(!_hasBeenInitialized)	//Do not perform this when "recreating" the same instance
            {
                _partialReceiveCommand = BuildNewReceiveHandler(_matchCommandBuilders.Pop());
                _partialReceiveRecover = BuildNewReceiveHandler(_matchRecoverBuilders.Pop());
                _hasBeenInitialized = true;
            }
        }

        private PartialAction<object> BuildNewReceiveHandler(MatchBuilder matchBuilder)
        {
            return matchBuilder.Build();
        }

        /// <summary>
        /// Creates and pushes a new MatchBuilder
        /// </summary>
        private void PrepareConfigureMessageHandlers()
        {
            _matchCommandBuilders.Push(new MatchBuilder(CachedMatchCompiler<object>.Instance));
            _matchRecoverBuilders.Push(new MatchBuilder(CachedMatchCompiler<object>.Instance));
        }

        protected sealed override void OnCommand(object message)
        {
            ExecutePartialMessageHandler(message, _partialReceiveCommand);
        }

        protected sealed override void OnRecover(object message)
        {
            ExecutePartialMessageHandler(message, _partialReceiveRecover);
        }

        private void ExecutePartialMessageHandler(object message, PartialAction<object> partialAction)
        {
            var wasHandled = partialAction(message);
            if (!wasHandled && _shouldUnhandle)
                Unhandled(message);
        }

        #region Recover helper methods

        private void EnsureMayConfigureRecoverHandlers()
        {
            if (_matchRecoverBuilders.Count <= 0)
                throw new InvalidOperationException("You may only call Recover-methods when constructing the actor and inside Become().");
        }

        protected void Recover<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        protected void Recover<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Recover<T>(handler, shouldHandle);
        }

        protected void Recover(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match(messageType, handler, shouldHandle);
        }

        protected void Recover(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Recover(messageType, handler, shouldHandle);
        }

        protected void Recover<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match<T>(handler);
        }

        protected void Recover(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match(messageType, handler);
        }

        protected void RecoverAny(Action<object> handler)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().MatchAny(handler);
        }

        #endregion

        #region Command helper methods
        
        private void EnsureMayConfigureCommandHandlers()
        {
            if (_matchCommandBuilders.Count <= 0)
                throw new InvalidOperationException("You may only call Command-methods when constructing the actor and inside Become().");
        }

        protected void Command<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        protected void Command<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Command<T>(handler, shouldHandle);
        }

        protected void Command(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match(messageType, handler, shouldHandle);
        }

        protected void Command(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Command(messageType, handler, shouldHandle);
        }

        protected void Command<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match<T>(handler);
        }

        protected void Command(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match(messageType, handler);
        }

        protected void Command(Action<object> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().MatchAny(handler);
        }

        #endregion
    }
}