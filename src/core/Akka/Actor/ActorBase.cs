using System;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
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
            public readonly object Status;

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
            public readonly Exception Cause;

            public Failure(Exception cause)
            {
                Cause = cause;
            }

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
    public interface IActorLogging
    {
        LoggingAdapter Log { get; }
    }

    /// <summary>
    /// Contains things needed by the framework
    /// </summary>
    public interface IInternalActor
    {
        /// <summary>Gets the context for this instance.</summary>
        /// <value>The context.</value>
        /// <exception cref="System.NotSupportedException">
        /// There is no active Context, this is most likely due to use of async
        /// operations from within this actor.
        /// </exception>
        IActorContext ActorContext { get; }
    }

    /// <summary>
    ///     Class ActorBase.
    /// </summary>
    public abstract partial class ActorBase : IInternalActor
    {
        private ActorRef _clearedSelf;
        private bool _hasBeenCleared;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorBase" /> class.
        /// </summary>
        /// <exception cref="System.Exception">Do not create actors using 'new', always create them using an ActorContext/System</exception>
        protected ActorBase()
        {
            if (ActorCell.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(Receive);
        }

        /// <summary>
        ///     Gets the sending ActorRef of the current message
        /// </summary>
        /// <value>The sender ActorRef</value>
        protected ActorRef Sender
        {
            get { return Context.Sender; }
        }

        /// <summary>
        ///     Gets the self ActorRef
        /// </summary>
        /// <value>Self ActorRef</value>
        protected ActorRef Self { get { return _hasBeenCleared?  _clearedSelf : Context.Self; } }

        /// <summary>
        ///     Gets the context.
        /// </summary>
        /// <value>The context.</value>
        /// <exception cref="System.NotSupportedException">
        ///     There is no active ActorContext, this is most likely due to use of async
        ///     operations from within this actor.
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
        /// <exception cref="System.NotSupportedException">
        ///     There is no active Context, this is most likely due to use of async
        ///     operations from within this actor.
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
        protected abstract bool Receive(object message);

        /// <summary>
        /// EmptyReceive is a Receive-delegate that matches no messages at all, ever.
        /// </summary>
        protected static Receive EmptyReceive { get { return _=> false; } }

        /// <summary>
        /// Is called when a message isn't handled by the current behavior of the actor
        /// by default it fails with either a <see cref="DeathPactException"/> (in
        /// case of an unhandled <see cref="Terminated"/> message) or publishes an <see cref="UnhandledMessage"/>
        /// to the actor's system's <see cref="EventStream"/>
        /// </summary>
        /// <param name="message">The unhandled message.</param>
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
        /// Changes the Actor's behavior to become the new <see cref="Actor.Receive"/> handler.
        /// This method acts upon the behavior stack as follows:
        /// <para>if <paramref name="discardOld"/>==<c>true</c> it will replace the current behavior (i.e. the top element)</para>
        /// <para>if <paramref name="discardOld"/>==<c>false</c> it will keep the current behavior and push the given one atop</para>
        /// The default of replacing the current behavior on the stack has been chosen to avoid memory
        /// leaks in case client code is written without consulting this documentation first (i.e.
        /// always pushing new behaviors and never issuing an <see cref="Unbecome"/>)
        /// </summary>
        /// <param name="receive">The receive delegate.</param>
        /// <param name="discardOld">If <c>true</c> it will replace the current behavior; 
        /// otherwise it will keep the current behavior and it can be reverted using <see cref="Unbecome"/></param>
        protected void Become(Receive receive, bool discardOld = true)
        {
            Context.Become(receive, discardOld);
        }

        /// <summary>
        /// Reverts the Actor behavior to the previous one on the behavior stack.
        /// </summary>
        protected void Unbecome()
        {
            Context.Unbecome();
        }

        internal void Clear(ActorRef self)
        {
            _hasBeenCleared = true;
            _clearedSelf = self;
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