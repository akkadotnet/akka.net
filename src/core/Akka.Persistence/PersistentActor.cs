//-----------------------------------------------------------------------
// <copyright file="PersistentActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Tools.MatchHandler;

namespace Akka.Persistence
{
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
    /// Recovery mode configuration object to be return in <see cref="PersistentActor.get_Recovery()"/>
    /// 
    /// By default recovers from latest snashot replays through to the last available event (last sequenceNr).
    /// 
    /// Recovery will start from a snapshot if the persistent actor has previously saved one or more snapshots
    /// and at least one of these snapshots matches the specified <see cref="FromSnapshot"/> criteria.
    /// Otherwise, recovery will start from scratch by replaying all stored events.
    /// 
    /// If recovery starts from a snapshot, the <see cref="PersistentActor"/> is offered that snapshot with a
    /// <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger than the snapshot, up to the
    /// specified upper sequence number bound (<see cref="ToSequenceNr"/>).
    /// </summary>
    [Serializable]
    public sealed class Recovery
    {
        public static readonly Recovery Default = new Recovery(SnapshotSelectionCriteria.Latest);
        public static readonly Recovery None = new Recovery(SnapshotSelectionCriteria.Latest, 0);

        public Recovery() : this(SnapshotSelectionCriteria.Latest)
        {
        }

        public Recovery(SnapshotSelectionCriteria fromSnapshot) : this(fromSnapshot, long.MaxValue)
        {
        }

        public Recovery(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr) : this(fromSnapshot, toSequenceNr, long.MaxValue)
        {
        }

        public Recovery(SnapshotSelectionCriteria fromSnapshot = null, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue)
        {
            FromSnapshot = fromSnapshot != null ? fromSnapshot : SnapshotSelectionCriteria.Latest;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// Criteria for selecting a saved snapshot from which recovery should start. Default is latest (= youngest) snapshot.
        /// </summary>
        public SnapshotSelectionCriteria FromSnapshot { get; private set; }

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery. Default is no upper bound.
        /// </summary>
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// Maximum number of messages to replay. Default is no limit.
        /// </summary>
        public long ReplayMax { get; private set; }
    }

    /// <summary>
    /// This defines how to handle the current received message which failed to stash, when the size
    /// of the Stash exceeding the capacity of the Stash.
    /// </summary>
    public interface IStashOverflowStrategy { }

    /// <summary>
    /// Discard the message to <see cref="DeadLetterActorRef"/>
    /// </summary>
    public class DiscardToDeadLetterStrategy : IStashOverflowStrategy
    {
        public static readonly DiscardToDeadLetterStrategy Instance = new DiscardToDeadLetterStrategy();

        private DiscardToDeadLetterStrategy() { }
    }

    /// <summary>
    /// Throw <see cref="StashOverflowException"/>, hence the persistent actor will start recovery
    /// if guarded by default supervisor strategy.
    /// Be careful if used together with <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/>
    /// or <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>
    /// or has many messages needed to replay.
    /// </summary>
    public class ThrowOverflowExceptionStrategy : IStashOverflowStrategy
    {
        public static readonly ThrowOverflowExceptionStrategy Instance = new ThrowOverflowExceptionStrategy();

        private ThrowOverflowExceptionStrategy() { }
    }

    /// <summary>
    /// Reply to sender with predefined response, and discard the received message silently.
    /// </summary>
    public sealed class ReplyToStrategy : IStashOverflowStrategy
    {
        public ReplyToStrategy(object response)
        {
            Response = response;
        }

        /// <summary>
        /// The message replying to sender with
        /// </summary>
        public object Response { get; private set; }
    }

    /// <summary>
    /// Implement this interface in order to configure the <see cref="IStashOverflowStrategy"/>
    /// for the internal stash of the persistent actor.
    /// An instance of this class must be instantiable using a no-args constructor.
    /// </summary>
    public interface IStashOverflowStrategyConfigurator
    {
        IStashOverflowStrategy Create(Config config);
    }

    public sealed class ThrowExceptionConfigurator : IStashOverflowStrategyConfigurator
    {
        public IStashOverflowStrategy Create(Config config)
        {
            return ThrowOverflowExceptionStrategy.Instance;
        }
    }

    public sealed class DiscardConfigurator : IStashOverflowStrategyConfigurator
    {
        public IStashOverflowStrategy Create(Config config)
        {
            return DiscardToDeadLetterStrategy.Instance;
        }
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
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void BecomeStacked(UntypedReceive receive)
        {
            Context.BecomeStacked(receive);
        }


        protected static new IUntypedActorContext Context { get { return (IUntypedActorContext)ActorBase.Context; } }
    }

    public abstract class ReceivePersistentActor : UntypedPersistentActor, IInitializableActor
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

        void IInitializableActor.Init()
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

        /// <summary>
        /// Changes the actor's command behavior and replaces the current receive command handler with the specified handler.
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Receive overloads.</param>
        protected void Become(Action configure)
        {
            var newHandler = CreateNewHandler(configure);
            base.Become(m => ExecutePartialMessageHandler(m, newHandler));
        }

        /// <summary>
        /// Changes the actor's command behavior and replaces the current receive command handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="ActorBase.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="ActorBase.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Command overloads.</param>
        protected void BecomeStacked(Action configure)
        {
            var newHandler = CreateNewHandler(configure);
            base.BecomeStacked(m => ExecutePartialMessageHandler(m, newHandler));
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

        protected void CommandAny(Action<object> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().MatchAny(handler);
        }

        private PartialAction<object> CreateNewHandler(Action configure)
        {
            _matchCommandBuilders.Push(new MatchBuilder(CachedMatchCompiler<object>.Instance));
            configure();
            var newHandler = BuildNewReceiveHandler(_matchCommandBuilders.Pop());
            return newHandler;
        }

        #endregion
    }
}

