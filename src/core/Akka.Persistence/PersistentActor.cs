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
using Akka.Tools.MatchHandler;

namespace Akka.Persistence
{
    /// <summary>
    /// Persistent actor - can be used to implement command or eventsourcing.
    /// </summary>
    public abstract class PersistentActor : Eventsourced
    {
        /// <inheritdoc />
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
        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            return ReceiveCommand(message);
        }

        /// <inheritdoc />
        protected sealed override bool ReceiveCommand(object message)
        {
            OnCommand(message);
            return true;
        }

        /// <inheritdoc />
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
        /// TBD
        /// </summary>
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

        /// <inheritdoc />
        protected sealed override void OnCommand(object message)
        {
            ExecutePartialMessageHandler(message, _partialReceiveCommand);
        }

        /// <inheritdoc />
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

        private void EnsureMayConfigureCommandHandlers()
        {
            if (_matchCommandBuilders.Count <= 0)
                throw new InvalidOperationException("You may only call Command-methods when constructing the actor and inside Become().");
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
    }
}
