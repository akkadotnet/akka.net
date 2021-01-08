//-----------------------------------------------------------------------
// <copyright file="ReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Tools.MatchHandler;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ReceiveActor : UntypedActor, IInitializableActor
    {
        private bool _shouldUnhandle = true;
        private readonly Stack<MatchBuilder> _matchHandlerBuilders = new Stack<MatchBuilder>();
        private PartialAction<object> _partialReceive = _ => false;
        private bool _hasBeenInitialized;

        /// <summary>
        /// TBD
        /// </summary>
        protected ReceiveActor()
        {
            PrepareConfigureMessageHandlers();
        }

        void IInitializableActor.Init()
        {
            //This might be called directly after the constructor, or when the same actor instance has been returned
            //during recreate. Make sure what happens here is idempotent
            if(!_hasBeenInitialized)	//Do not perform this when "recreating" the same instance
            {
                _partialReceive = BuildNewReceiveHandler(_matchHandlerBuilders.Pop());
                _hasBeenInitialized = true;
            }
        }

        private PartialAction<object> BuildNewReceiveHandler(MatchBuilder matchBuilder)
        {
            return matchBuilder.Build();
        }


        private void EnsureMayConfigureMessageHandlers()
        {
            if(_matchHandlerBuilders.Count <= 0) throw new InvalidOperationException("You may only call Receive-methods when constructing the actor and inside Become().");
        }

        /// <summary>
        /// Creates and pushes a new MatchBuilder
        /// </summary>
        private void PrepareConfigureMessageHandlers()
        {
            _matchHandlerBuilders.Push(new MatchBuilder(CachedMatchCompiler<object>.Instance));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected sealed override void OnReceive(object message)
        {
            //Seal the method so that implementors cannot use it. They should only use Receive and Become

            ExecutePartialMessageHandler(message, _partialReceive);
        }

        private void ExecutePartialMessageHandler(object message, PartialAction<object> partialAction)
        {
            var wasHandled = partialAction(message);
            if(!wasHandled && _shouldUnhandle)
                Unhandled(message);
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Receive overloads.</param>
        protected void Become(Action configure)
        {
            var newHandler = CreateNewHandler(configure);
            base.Become(m => ExecutePartialMessageHandler(m, newHandler));
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="ActorBase.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="ActorBase.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="configure">Configures the new handler by calling the different Receive overloads.</param>
        protected void BecomeStacked(Action configure)
        {
            var newHandler = CreateNewHandler(configure);
            base.BecomeStacked(m => ExecutePartialMessageHandler(m, newHandler));
        }

        private PartialAction<object> CreateNewHandler(Action configure)
        {
            PrepareConfigureMessageHandlers();
            configure();
            var newHandler = BuildNewReceiveHandler(_matchHandlerBuilders.Pop());
            return newHandler;
        }

        private Action<T> WrapAsyncHandler<T>(Func<T, Task> asyncHandler)
        {
            return m =>
            {
                Task Wrap() => asyncHandler(m);
                ActorTaskScheduler.RunTask(Wrap);
            };
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void ReceiveAsync<T>(Func<T,Task> handler, Predicate<T> shouldHandle = null)
        {
            Receive(WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(System.Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        protected void ReceiveAsync<T>(Predicate<T> shouldHandle, Func<T, Task> handler)
        {
            Receive(WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void ReceiveAsync(Type messageType, Func<object, Task> handler, Predicate<object> shouldHandle = null)
        {
            Receive(messageType, WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        protected void ReceiveAsync(Type messageType, Predicate<object> shouldHandle, Func<object, Task> handler)
        {
            Receive(messageType, WrapAsyncHandler(handler), shouldHandle);
        }

        /// <summary>
        /// Registers an asynchronous handler for incoming messages of any type.
        /// <remarks>The actor will be suspended until the task returned by <paramref name="handler"/> completes.</remarks>
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        protected void ReceiveAnyAsync(Func<object, Task> handler)
        {
            ReceiveAny(WrapAsyncHandler(handler));
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Receive<T>(handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <paramref name="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Receive(messageType, handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <typeparamref name="T"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>(handler);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <paramref name="messageType"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <paramref name="messageType"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void Receive(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match(messageType, handler);
        }

        /// <summary>
        /// Registers a handler for incoming messages of any type.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become(Action)"/> or <see cref="BecomeStacked"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if this method is called outside of the actor's constructor or from <see cref="Become(Action)"/>.</exception>
        protected void ReceiveAny(Action<object> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().MatchAny(handler);
        }
    }
}
