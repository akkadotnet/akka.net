//-----------------------------------------------------------------------
// <copyright file="PersistentActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Tools.MatchHandler;
using System.Threading.Tasks;

namespace Akka.Persistence
{
    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> when the journal replay has been finished.
    /// </summary>
    [Serializable]
    public sealed class RecoveryCompleted
    {
        /// <summary>
        /// The singleton instance of <see cref="RecoveryCompleted"/>.
        /// </summary>
        public static readonly RecoveryCompleted Instance = new RecoveryCompleted();
        private RecoveryCompleted(){}

        public override bool Equals(object obj) => obj is RecoveryCompleted;
        public override int GetHashCode() => nameof(RecoveryCompleted).GetHashCode();
    }

    /// <summary>
    /// Recovery mode configuration object to be return in <see cref="Eventsourced.Recovery"/>
    /// 
    /// By default recovers from latest snapshot replays through to the last available event (last sequenceNr).
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
        /// <summary>
        /// TBD
        /// </summary>
        public static Recovery Default { get; } = new Recovery(SnapshotSelectionCriteria.Latest);

        /// <summary>
        /// Convenience method for skipping recovery in <see cref="PersistentActor"/>.
        /// 
        /// It will still retrieve previously highest sequence number so that new events are persisted with
        /// higher sequence numbers rather than starting from 1 and assuming that there are no
        /// previous event with that sequence number.
        /// </summary>
        public static Recovery None { get; } = new Recovery(SnapshotSelectionCriteria.None, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        public Recovery() : this(SnapshotSelectionCriteria.Latest)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start. Default is latest(= youngest) snapshot.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot) : this(fromSnapshot, long.MaxValue)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start. Default is latest(= youngest) snapshot.</param>
        /// <param name="toSequenceNr">Upper, inclusive sequence number bound for recovery. Default is no upper bound.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr) : this(fromSnapshot, toSequenceNr, long.MaxValue)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start. Default is latest(= youngest) snapshot.</param>
        /// <param name="toSequenceNr">Upper, inclusive sequence number bound for recovery. Default is no upper bound.</param>
        /// <param name="replayMax">Maximum number of messages to replay. Default is no limit.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot = null, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue)
        {
            FromSnapshot = fromSnapshot ?? SnapshotSelectionCriteria.Latest;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// Criteria for selecting a saved snapshot from which recovery should start. Default is latest (= youngest) snapshot.
        /// </summary>
        public SnapshotSelectionCriteria FromSnapshot { get; }

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery. Default is no upper bound.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <summary>
        /// Maximum number of messages to replay. Default is no limit.
        /// </summary>
        public long ReplayMax { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class RecoveryTimedOutException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        public RecoveryTimedOutException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public RecoveryTimedOutException(string message, Exception cause = null) : base(message, cause)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public RecoveryTimedOutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
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
        /// <summary>
        /// The singleton instance of <see cref="DiscardToDeadLetterStrategy"/>.
        /// </summary>
        public static DiscardToDeadLetterStrategy Instance { get; } = new DiscardToDeadLetterStrategy();

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
        /// <summary>
        /// The singleton instance of <see cref="ThrowOverflowExceptionStrategy"/>.
        /// </summary>
        public static ThrowOverflowExceptionStrategy Instance { get; } = new ThrowOverflowExceptionStrategy();

        private ThrowOverflowExceptionStrategy() { }
    }

    /// <summary>
    /// Reply to sender with predefined response, and discard the received message silently.
    /// </summary>
    public sealed class ReplyToStrategy : IStashOverflowStrategy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplyToStrategy"/> class.
        /// </summary>
        /// <param name="response">TBD</param>
        public ReplyToStrategy(object response)
        {
            Response = response;
        }

        /// <summary>
        /// The message replying to sender with
        /// </summary>
        public object Response { get; }
    }

    /// <summary>
    /// Implement this interface in order to configure the <see cref="IStashOverflowStrategy"/>
    /// for the internal stash of the persistent actor.
    /// An instance of this class must be instantiable using a no-args constructor.
    /// </summary>
    public interface IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        IStashOverflowStrategy Create(Config config);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ThrowExceptionConfigurator : IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public IStashOverflowStrategy Create(Config config)
        {
            return ThrowOverflowExceptionStrategy.Instance;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class DiscardConfigurator : IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
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
        /// <inheritdoc/>
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
        /// <inheritdoc/>
        protected override bool Receive(object message)
        {
            return ReceiveCommand(message);
        }

        /// <inheritdoc/>
        protected sealed override bool ReceiveCommand(object message)
        {
            OnCommand(message);
            return true;
        }

        /// <inheritdoc/>
        protected sealed override bool ReceiveRecover(object message)
        {
            OnRecover(message);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected abstract void OnCommand(object message);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected abstract void OnRecover(object message);

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

        /// <summary>
        /// TBD
        /// </summary>
        protected new static IUntypedActorContext Context => (IUntypedActorContext)ActorBase.Context;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ReceivePersistentActor : UntypedPersistentActor, IInitializableActor
    {
        private bool _shouldUnhandle = true;
        private readonly Stack<MatchBuilder> _matchCommandBuilders = new Stack<MatchBuilder>();
        private readonly Stack<MatchBuilder> _matchRecoverBuilders = new Stack<MatchBuilder>();
        private PartialAction<object> _partialReceiveCommand = _ => false;
        private PartialAction<object> _partialReceiveRecover = _ => false;
        private bool _hasBeenInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceivePersistentActor"/> class.
        /// </summary>
        protected ReceivePersistentActor()
        {
            PrepareConfigureMessageHandlers();
        }

        void IInitializableActor.Init()
        {
            //This might be called directly after the constructor, or when the same actor instance has been returned
            //during recreate. Make sure what happens here is idempotent
            if (!_hasBeenInitialized)	//Do not perform this when "recreating" the same instance
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

        /// <inheritdoc/>
        protected sealed override void OnCommand(object message)
        {
            ExecutePartialMessageHandler(message, _partialReceiveCommand);
        }

        /// <inheritdoc/>
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

        private Action<T> WrapAsyncHandler<T>(Func<T, Task> asyncHandler)
        {
            return m =>
            {
                Func<Task> wrap = () => asyncHandler(m);
                RunTask(wrap);
            };
        }

        #region Recover helper methods

        private void EnsureMayConfigureRecoverHandlers()
        {
            if (_matchRecoverBuilders.Count <= 0)
                throw new InvalidOperationException("You may only call Recover-methods when constructing the actor and inside Become().");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        protected void Recover<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Recover<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Recover<T>(handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        protected void Recover(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Recover(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Recover(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        protected void Recover<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match<T>(handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Recover(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureRecoverHandlers();
            _matchRecoverBuilders.Peek().Match(messageType, handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
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

        /// <summary>
        /// Registers an asynchronous handler for incoming command of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" /> and
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already.
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void CommandAsync<T>(Func<T, Task> handler, Predicate<T> shouldHandle = null)
        {
            Command(WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming command of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" /> and
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already.
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        protected void CommandAsync<T>(Predicate<T> shouldHandle, Func<T, Task> handler)
        {
            Command(shouldHandle, WrapAsyncHandler(handler));
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming command of the specified type <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" /> and
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already.
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void CommandAsync(Type messageType, Func<object, Task> handler, Predicate<object> shouldHandle = null)
        {
            Command(messageType, WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming command of the specified type <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" /> and
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already.
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <paramref name="messageType"/></param>
        protected void CommandAsync(Type messageType, Predicate<object> shouldHandle, Func<object, Task> handler)
        {
            Command(messageType, shouldHandle, WrapAsyncHandler(handler));
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming command of any type.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" /> and
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already.
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for incoming messages of any type</param>
        protected void CommandAnyAsync(Func<object, Task> handler)
        {
            CommandAny(WrapAsyncHandler(handler));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        protected void Command<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Command<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Command<T>(handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        protected void Command(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Command(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Command(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        protected void Command<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match<T>(handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageType">TBD</param>
        /// <param name="handler">TBD</param>
        protected void Command(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().Match(messageType, handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        protected void Command(Action<object> handler)
        {
            EnsureMayConfigureCommandHandlers();
            _matchCommandBuilders.Peek().MatchAny(handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
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
