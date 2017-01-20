﻿//-----------------------------------------------------------------------
// <copyright file="ActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor.Internal;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Classes for passing status back to the sender.
    /// Used for internal ACKing protocol, but also exposed as a utility class for user-specific ACKing if needed.
    /// </summary>
    public abstract class Status
    {
        /// <summary>
        /// Indicates the success of some operation which has been performed
        /// </summary>
        public class Success : Status
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly object Status;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="status">TBD</param>
            public Success(object status)
            {
                Status = status;
            }
        }
        
        /// <summary>
        /// Indicates the failure of some operation that was requested and includes an
        /// <see cref="Exception"/> describing the underlying cause of the problem.
        /// </summary>
        public class Failure : Status
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Exception Cause;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public Failure(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString()
            {
                return "Failure: " + Cause.ToString();
            }
        }
    }

    /// <summary>
    ///     Interface ILogReceive
    /// </summary>
    public interface ILogReceive
    {
    }

    /// <summary>
    /// Interface used on Actors that have an explicit requirement for a logger
    /// </summary>
    [Obsolete()]
    public interface IActorLogging
    {
        /// <summary>
        /// TBD
        /// </summary>
        ILoggingAdapter Log { get; }
    }

    /// <summary>
    /// Contains things needed by the framework
    /// </summary>
    public interface IInternalActor
    {
        /// <summary>Gets the context for this instance.</summary>
        /// <value>The context.</value>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if there is no active Context. The most likely cause is due to use of async operations from within this actor.
        /// </exception>
        IActorContext ActorContext { get; }
    }

    /// <summary>
    ///     Class ActorBase.
    /// </summary>
    public abstract partial class ActorBase : IInternalActor
    {
        private IActorRef _clearedSelf;
        private bool HasBeenCleared { get { return _clearedSelf != null; } }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorBase" /> class.
        /// </summary>
        /// <exception cref="ActorInitializationException">
        /// This exception is thrown when an actor is created using <c>new</c>. Always create actors using an ActorContext/System.
        /// </exception>
        protected ActorBase()
        {
            if (ActorCell.Current == null)
                throw new ActorInitializationException("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(Receive);
        }

        /// <summary>
        ///     Gets the sending ActorRef of the current message
        /// </summary>
        /// <value>The sender ActorRef</value>
        protected IActorRef Sender
        {
            get { return Context.Sender; }
        }

        /// <summary>
        ///     Gets the self ActorRef
        /// </summary>
        /// <value>Self ActorRef</value>
        protected IActorRef Self { get { return HasBeenCleared ? _clearedSelf : Context.Self; } }

        /// <summary>
        ///     Gets the context.
        /// </summary>
        /// <value>The context.</value>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if there is no active ActorContext. The most likely cause is due to use of async operations from within this actor.
        /// </exception>
        IActorContext IInternalActor.ActorContext
        {
            get {
                return Context;
            }
        }

        /// <summary>
        ///     Gets the context.
        /// </summary>
        /// <value>The context.</value>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if there is no active Context. The most likely cause is due to use of async operations from within this actor.
        /// </exception>
        protected static IActorContext Context
        {
            get
            {
                var context = InternalCurrentActorCellKeeper.Current;
                if (context == null)
                    throw new NotSupportedException(
                        "There is no active ActorContext, this is most likely due to use of async operations from within this actor.");

                return context.ActorHasBeenCleared ? null : context;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        internal protected virtual bool AroundReceive(Receive receive, object message)
        {
            var wasHandled = receive(message);
            if(!wasHandled)
            {
                Unhandled(message);
            }
            return wasHandled;
        }

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns>TBD</returns>
        protected abstract bool Receive(object message);

        /// <summary>
        /// EmptyReceive is a Receive-delegate that matches no messages at all, ever.
        /// </summary>
        protected static Receive EmptyReceive { get { return _ => false; } }

        /// <summary>
        /// Is called when a message isn't handled by the current behavior of the actor
        /// by default it fails with either a <see cref="DeathPactException"/> (in
        /// case of an unhandled <see cref="Terminated"/> message) or publishes an <see cref="UnhandledMessage"/>
        /// to the actor's system's <see cref="EventStream"/>
        /// </summary>
        /// <param name="message">The unhandled message.</param>
        /// <exception cref="DeathPactException">
        /// This exception is thrown if the given <paramref name="message"/> is a <see cref="Terminated"/> message.
        /// </exception>
        protected virtual void Unhandled(object message)
        {
            var terminatedMessage = message as Terminated;
            if(terminatedMessage != null)
            {
                throw new DeathPactException(terminatedMessage.ActorRef);
            }
            Context.System.EventStream.Publish(new UnhandledMessage(message, Sender, Self));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="discardOld">TBD</param>
        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        protected void Become(Receive receive, bool discardOld = true)
        {
            if(discardOld)
                Context.Become(receive);
            else
                Context.BecomeStacked(receive);
        }

        /// <summary>
        /// Changes the actor's command behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void Become(Receive receive)
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
        protected void BecomeStacked(Receive receive)
        {
            Context.BecomeStacked(receive);
        }

        /// <summary>
        /// Reverts the Actor behavior to the previous one on the behavior stack.
        /// </summary>
        protected void UnbecomeStacked()
        {
            Context.UnbecomeStacked();
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use UnbecomeStacked instead. This method will be removed in future versions")]
        protected void Unbecome()
        {
            UnbecomeStacked();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        internal void Clear(IActorRef self)
        {
            _clearedSelf = self;
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal void Unclear()
        {
            _clearedSelf = null;
        }


        /// <summary>
        /// <para>
        /// Defines the inactivity timeout after which the sending of a <see cref="ReceiveTimeout"/> message is triggered.
        /// When specified, the receive function should be able to handle a <see cref="ReceiveTimeout"/> message.
        /// </para>
        /// 
        /// <para>
        /// Please note that the receive timeout might fire and enqueue the <see cref="ReceiveTimeout"/> message right after
        /// another message was enqueued; hence it is not guaranteed that upon reception of the receive
        /// timeout there must have been an idle period beforehand as configured via this method.
        /// </para>
        /// 
        /// <para>
        /// Once set, the receive timeout stays in effect (i.e. continues firing repeatedly after inactivity
        /// periods). Pass in <c>null</c> to switch off this feature.
        /// </para>
        /// </summary>
        /// <param name="timeout">The timeout. Pass in <c>null</c> to switch off this feature.</param>
        protected void SetReceiveTimeout(TimeSpan? timeout)
        {
            Context.SetReceiveTimeout(timeout);
        }
    }
}

