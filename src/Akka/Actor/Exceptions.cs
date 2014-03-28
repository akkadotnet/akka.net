using System;

namespace Akka.Actor
{
    /// <summary>
    ///     Class AkkaException.
    /// </summary>
    public abstract class AkkaException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AkkaException" /> class.
        /// </summary>
        protected AkkaException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="T:System.Exception" /> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">An inner exception responsible for this error.</param>
        protected AkkaException(string message, Exception cause = null)
            : base(message, cause)
        {
        }
    }

    /// <summary>
    ///     Class ActorInitializationException.
    /// </summary>
    public class ActorInitializationException : AkkaException
    {
    }

    /// <summary>
    ///     Class ActorKilledException.
    /// </summary>
    public class ActorKilledException : AkkaException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorKilledException" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public ActorKilledException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// IllegalActorStateException is thrown when a core invariant in the Actor implementation has been voilated.
    /// For instance, if you try to create an Actor that doesn't inherit from <see cref="ActorBase"/>.
    /// </summary>
    public class IllegalActorStateException : AkkaException
    {
        public IllegalActorStateException(string msg) : base(msg) { }
    }

    /// <summary>
    ///     Class DeathPactException.
    /// </summary>
    public class DeathPactException : AkkaException
    {
    }

    /// <summary>
    ///     Class PreRestartException.
    /// </summary>
    public class PreRestartException : AkkaException
    {
        private LocalActorRef Actor;
        private Exception e; //TODO: what is this?
        private Exception exception;
        private object optionalMessage;

        public PreRestartException(LocalActorRef actor, Exception restartException, Exception cause,
            object optionalMessage)
        {
            Actor = actor;
            e = restartException;
            exception = cause;
            this.optionalMessage = optionalMessage;
        }
    }
}