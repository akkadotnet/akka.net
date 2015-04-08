using System;
using System.Runtime.Serialization;

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

        protected AkkaException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        protected Exception Cause { get { return InnerException; } }
    }

    /// <summary>
    /// An InvalidActorNameException is thrown when the actor name is invalid
    /// </summary>
    public class InvalidActorNameException : AkkaException
    {
        public InvalidActorNameException(string message)
            : base(message)
        {
            //Intentionally left blank
        }

        public InvalidActorNameException(string message, Exception innerException)
            : base(message, innerException)
        {
            //Intentionally left blank
        }

        protected InvalidActorNameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Thrown when an Ask operation times out
    /// </summary>
    public class AskTimeoutException : AkkaException
    {
        public AskTimeoutException(string message)
            : base(message)
        {
            //Intentionally left blank
        }

        protected AskTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// </summary>
    public class ActorInitializationException : AkkaException
    {
        private readonly IActorRef _actor;
        protected ActorInitializationException() : base(){}

        public ActorInitializationException(string message) : base(message) { }

        public ActorInitializationException(string message, Exception cause) : base(message, cause) { }
        public ActorInitializationException(IActorRef actor, string message, Exception cause = null) : base(message, cause)
        {
            _actor = actor;
        }

        protected ActorInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        public IActorRef Actor { get { return _actor; } }

        public override string ToString()
        {
            if (_actor == null) return base.ToString();
            return _actor + ": " + base.ToString();
        }
    }

    /// <summary>
    ///     Class LoggerInitializationException is thrown to indicate that there was a problem initializing a logger.
    /// </summary>
    public class LoggerInitializationException : AkkaException
    {
        public LoggerInitializationException() : base() { }

        public LoggerInitializationException(string message) : base(message) { }

        public LoggerInitializationException(string message, Exception cause = null) : base(message, cause) { }

        protected LoggerInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }



    /// <summary>
    /// Thrown when a <see cref="Kill"/> message has been sent to an actor. <see cref="SupervisorStrategy.DefaultDecider"/> will by default stop the actor.
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

        protected ActorKilledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// IllegalActorStateException is thrown when a core invariant in the Actor implementation has been violated.
    /// For instance, if you try to create an Actor that doesn't inherit from <see cref="ActorBase"/>.
    /// </summary>
    public class IllegalActorStateException : AkkaException
    {
        public IllegalActorStateException(string msg) : base(msg) { }

        protected IllegalActorStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// IllegalActorNameException is thrown when an Actor with an invalid name is deployed our bound.
    /// </summary>
    public class IllegalActorNameException : AkkaException
    {
        public IllegalActorNameException(string msg) : base(msg) { }

        protected IllegalActorNameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// A DeathPactException is thrown by an Actor that receives a Terminated(someActor) message
    /// that it doesn't handle itself, effectively crashing the Actor and escalating to the supervisor.
    /// </summary>
    public class DeathPactException : AkkaException
    {
        private readonly IActorRef _deadActor;

        public DeathPactException(IActorRef deadActor)
            : base("Monitored actor [" + deadActor + "] terminated")
        {
            _deadActor = deadActor;
        }

        protected DeathPactException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        public IActorRef DeadActor
        {
            get { return _deadActor; }
        }
    }

    /// <summary>
    ///     Class PreRestartException.
    /// </summary>
    public class PreRestartException : AkkaException
    {
        private IActorRef Actor;
        private Exception e; //TODO: what is this?
        private Exception exception;
        private object optionalMessage;

        public PreRestartException(IActorRef actor, Exception restartException, Exception cause,
            object optionalMessage)
        {
            Actor = actor;
            e = restartException;
            exception = cause;
            this.optionalMessage = optionalMessage;
        }

        protected PreRestartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// A PostRestartException is thrown when constructor or postRestart() method
    /// fails during a restart attempt.
    /// <para><see cref="PostRestartException.Actor"/>: actor is the actor whose constructor or postRestart() hook failed.</para>
    /// <para><see cref="PostRestartException.Cause"/>: cause is the exception thrown by that actor within preRestart()</para>
    /// <para><see cref="OriginalCause"/>: originalCause is the exception which caused the restart in the first place</para>
    /// </summary>
    public class PostRestartException : ActorInitializationException
    {
        private readonly Exception _originalCause;

        /// <summary>
        /// Initializes a new instance of the <see cref="PostRestartException"/> class.
        /// </summary>
        /// <param name="actor">The actor whose constructor or postRestart() hook failed.</param>
        /// <param name="cause">Cause is the exception thrown by that actor within preRestart().</param>
        /// <param name="originalCause">The original cause is the exception which caused the restart in the first place.</param>
        public PostRestartException(IActorRef actor, Exception cause, Exception originalCause)
            :base(actor,"Exception post restart (" + (originalCause == null ?"null" : originalCause.GetType().ToString()) + ")", cause)
        {
            _originalCause = originalCause;
        }

        protected PostRestartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        public Exception OriginalCause { get { return _originalCause; } }
    }


    /// <summary>
    /// Class ActorNotFoundException.
    /// </summary>
    public class ActorNotFoundException : AkkaException
    {
        public ActorNotFoundException() : base() { }
        
        protected ActorNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// InvalidMessageException is thrown when an invalid message is sent to an Actor.
    /// Currently only <c>null</c> is an invalid message.
    /// </summary>
    public class InvalidMessageException : AkkaException
    {
        public InvalidMessageException() : this("Message is null")
        {
        }

        public InvalidMessageException(string message):base(message)
        {
        }

        protected InvalidMessageException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}