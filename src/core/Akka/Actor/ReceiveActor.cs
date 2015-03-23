using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Tools.MatchHandler;

namespace Akka.Actor
{

    public abstract class ReceiveActor : UntypedActor, InitializableActor
    {
        private bool _shouldUnhandle = true;
        private readonly Stack<MatchBuilder> _matchHandlerBuilders = new Stack<MatchBuilder>();
        private PartialAction<object> _partialReceive = _ => false;
        private bool _hasBeenInitialized;

        protected ReceiveActor()
        {
            PrepareConfigureMessageHandlers();
        }

        void InitializableActor.Init()
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

        //Seal the method so that implementors cannot use it. They should only use Receive and Become
        protected sealed override void OnReceive(object message)
        {
            ExecutePartialMessageHandler(message, _partialReceive);
        }

        private void ExecutePartialMessageHandler(object message, PartialAction<object> partialAction)
        {
            var wasHandled = partialAction(message);
            if(!wasHandled && _shouldUnhandle)
                Unhandled(message);
        }

        protected void Become(Action configure, bool discardOld = true)
        {
            PrepareConfigureMessageHandlers();
            configure();
            var newHandler = BuildNewReceiveHandler(_matchHandlerBuilders.Pop());
            base.Become(m => ExecutePartialMessageHandler(m, newHandler), discardOld);
        }

        protected void Receive<T>(Func<T,Task> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>( m =>
            {
                Func<Task> wrap = () => handler(m);
                ActorTaskScheduler.RunTask(AsyncBehavior.Suspend, wrap);
            });
        }

        protected void Receive<T>(AsyncBehavior behavior, Func<T, Task> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>(m =>
            {
                Func<Task> wrap = () => handler(m);
                ActorTaskScheduler.RunTask(behavior, wrap);
            });
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.        
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void Receive<T>(Action<T> handler, Predicate<T> shouldHandle = null)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>(handler, shouldHandle);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.        
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified type <typeparamref name="T"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void Receive<T>(Predicate<T> shouldHandle, Action<T> handler)
        {
            Receive<T>(handler, shouldHandle);
        }


        /// <summary>
        /// Registers a handler for incoming messages of the specified <see cref="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.        
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <see cref="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void Receive(Type messageType, Action<object> handler, Predicate<object> shouldHandle = null)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match(messageType, handler, shouldHandle);
        }


        /// <summary>
        /// Registers a handler for incoming messages of the specified <see cref="messageType"/>.
        /// If <paramref name="shouldHandle"/>!=<c>null</c> then it must return true before a message is passed to <paramref name="handler"/>.        
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the specified <see cref="messageType"/></param>
        /// <param name="shouldHandle">When not <c>null</c> it is used to determine if the message matches.</param>
        protected void Receive(Type messageType, Predicate<object> shouldHandle, Action<object> handler)
        {
            Receive(messageType, handler, shouldHandle);
        }





        /// <summary>
        /// Registers a handler for incoming messages of the specified type <typeparamref name="T"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type of the message</typeparam>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <typeparamref name="T"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        protected void Receive<T>(Func<T, bool> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match<T>(handler);
        }

        /// <summary>
        /// Registers a handler for incoming messages of the specified <see cref="messageType"/>.
        /// The handler should return <c>true</c> if it has handled the message. 
        /// If the handler returns true no more handlers will be tried; otherwise the next registered handler will be tried.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="messageType">The type of the message</param>
        /// <param name="handler">The message handler that is invoked for incoming messages of the 
        /// specified type <see cref="messageType"/>. It should return <c>true</c>if it handled/matched 
        /// the message; <c>false</c> otherwise.</param>
        protected void Receive(Type messageType, Func<object, bool> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().Match(messageType, handler);
        }





        /// <summary>
        /// Registers a handler for incoming messages of any type.
        /// <remarks>This method may only be called when constructing the actor or from <see cref="Become"/>.</remarks>
        /// <remarks>Note that handlers registered prior to this may have handled the message already. 
        /// In that case, this handler will not be invoked.</remarks>
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        protected void ReceiveAny(Action<object> handler)
        {
            EnsureMayConfigureMessageHandlers();
            _matchHandlerBuilders.Peek().MatchAny(handler);
        }

    }
}