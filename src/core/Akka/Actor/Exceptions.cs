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
        protected Exception Cause { get { return InnerException; } }
    }

    /// <summary>
    /// An ActorInitializationException is thrown when the the initialization logic for an Actor fails.
    /// which doesn't validate.
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
    }

    /// <summary>
    /// </summary>
    public class ActorInitializationException : AkkaException
    {
        private readonly ActorRef _actor;
        protected ActorInitializationException() : base(){}

        public ActorInitializationException(string message) : base(message) { }

        public ActorInitializationException(string message, Exception cause) : base(message, cause) { }
        public ActorInitializationException(ActorRef actor, string message, Exception cause = null) : base(message, cause)
        {
            _actor = actor;
        }

        public ActorRef Actor { get { return _actor; } }

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
        private ActorRef Actor;
        private Exception e; //TODO: what is this?
        private Exception exception;
        private object optionalMessage;

        public PreRestartException(ActorRef actor, Exception restartException, Exception cause,
            object optionalMessage)
        {
            Actor = actor;
            e = restartException;
            exception = cause;
            this.optionalMessage = optionalMessage;
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
        /// <param name="originalCause">The original causeis the exception which caused the restart in the first place.</param>
        public PostRestartException(ActorRef actor, Exception cause, Exception originalCause)
            :base(actor,"Exception post restart (" + (originalCause == null ?"null" : originalCause.GetType().ToString()) + ")", cause)
        {
            _originalCause = originalCause;
        }

        public Exception OriginalCause { get { return _originalCause; } }
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