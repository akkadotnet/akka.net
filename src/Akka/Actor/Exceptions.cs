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
    /// IllegalActorNameException is thrown when an Actor with an invalid name is deployed our bound.
    /// </summary>
    public class IllegalActorNameException : AkkaException
    {
        public IllegalActorNameException(string msg) : base(msg) { }
    }

    /// <summary>
    /// A DeathPactException is thrown by an Actor that receives a Terminated(someActor) message
    /// that it doesn't handle itself, effectively crashing the Actor and escalating to the supervisor.
    /// </summary>
    public class DeathPactException : AkkaException
    {
        private readonly ActorRef _deadActor;

        public DeathPactException(ActorRef deadActor)
            : base("Monitored actor [" + deadActor + "] terminated")
        {
            _deadActor = deadActor;
        }

        public ActorRef DeadActor
        {
            get { return _deadActor; }
        }
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


    /// <summary>
    /// Class ActorNotFoundException.
    /// </summary>
    public class ActorNotFoundException : AkkaException
    {
    }

    /// <summary>
    /// InvalidMessageException is thrown when an invalid message is sent to an Actor.
    /// Currently only <c>null</c> is an invalid message.
    /// </summary>
    public class InvalidMessageException:AkkaException
    {
        public InvalidMessageException() : this("Message is null")
        {
        }

        public InvalidMessageException(string message):base(message)
        {            
        }
    }
}