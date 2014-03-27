using System;
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
    ///     Class ActorBase.
    /// </summary>
    public abstract partial class ActorBase
    {
        /// <summary>
        ///     The last message is unhandled
        /// </summary>
        private bool lastMessageIsUnhandled;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorBase" /> class.
        /// </summary>
        /// <exception cref="System.Exception">Do not create actors using 'new', always create them using an ActorContext/System</exception>
        protected ActorBase()
        {
            if (ActorCell.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(OnReceive);
            ((ActorCell) Context).Actor = this;
            Self = Context.Self;
            ((ActorCell) Context).Start();
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
        protected LocalActorRef Self { get; private set; }

        /// <summary>
        ///     Gets the context.
        /// </summary>
        /// <value>The context.</value>
        /// <exception cref="System.NotSupportedException">
        ///     There is no active ActorContext, this is most likely due to use of async
        ///     operations from within this actor.
        /// </exception>
        protected static IActorContext Context
        {
            get
            {
                ActorCell context = ActorCell.Current;
                if (context == null)
                    throw new NotSupportedException(
                        "There is no active ActorContext, this is most likely due to use of async operations from within this actor.");

                return context;
            }
        }


        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void OnReceive(object message);

        /// <summary>
        ///     Gets a function that will tell if the last message was unhandled or not.
        /// </summary>
        /// <returns>Func{System.ObjectSystem.Boolean}.</returns>
        public Func<object, bool> GetUnhandled()
        {
            lastMessageIsUnhandled = false;

            return IsUnhandled;
        }

        /// <summary>
        ///     Determines whether the specified message is unhandled.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns><c>true</c> if the specified message is unhandled; otherwise, <c>false</c>.</returns>
        private bool IsUnhandled(object message)
        {
            return lastMessageIsUnhandled;
        }

        /// <summary>
        ///     Marks the message as unhandled.
        /// </summary>
        /// <param name="message">The message.</param>
        protected void Unhandled(object message)
        {
            lastMessageIsUnhandled = true;
            Context.System.EventStream.Publish(new UnhandledMessage(message, Sender, Self));
        }


        /// <summary>
        ///     Becomes the specified receive function.
        /// </summary>
        /// <param name="receive">The receive.</param>
        protected void Become(Receive receive)
        {
            Context.Become(receive);
        }

        /// <summary>
        ///     Unbecomes the current receive function.
        /// </summary>
        protected void Unbecome()
        {
            Context.Unbecome();
        }
    }
}