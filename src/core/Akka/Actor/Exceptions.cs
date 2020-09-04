//-----------------------------------------------------------------------
// <copyright file="Exceptions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Actor
{
    /// <summary>
    /// This exception provides the base for all Akka.NET specific exceptions within the system.
    /// </summary>
    public abstract class AkkaException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaException"/> class.
        /// </summary>
        protected AkkaException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        protected AkkaException(string message, Exception cause = null)
            : base(message, cause)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

        /// <summary>
        /// The exception that is the cause of the current exception.
        /// </summary>
        protected Exception Cause { get { return InnerException; } }
    }

    /// <summary>
    /// This exception is thrown when the actor name is invalid.
    /// </summary>
    public class InvalidActorNameException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidActorNameException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public InvalidActorNameException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidActorNameException"/> class.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public InvalidActorNameException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidActorNameException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected InvalidActorNameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when an Ask operation times out.
    /// </summary>
    public class AskTimeoutException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AskTimeoutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public AskTimeoutException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AskTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AskTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when an actor is interrupted in the midst of processing messages.
    /// 
    /// This is an exception typically thrown when the underlying dispatcher's threads are aborted.
    /// </summary>
    public class ActorInterruptedException : AkkaException
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="cause">TBD</param>
        public ActorInterruptedException(string message = null, Exception cause = null) : base(message, cause) { }
    }

    /// <summary>
    /// This exception is thrown when the initialization logic for an Actor fails.
    /// </summary>
    public class ActorInitializationException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorInitializationException"/> class.
        /// </summary>
        public ActorInitializationException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorInitializationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ActorInitializationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorInitializationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public ActorInitializationException(string message, Exception cause)
            : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorInitializationException"/> class.
        /// </summary>
        /// <param name="actor">The actor whose initialization logic failed.</param>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public ActorInitializationException(IActorRef actor, string message, Exception cause = null)
            : base(message, cause)
        {
            Actor = actor;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorInitializationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected ActorInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Actor = (IActorRef)info.GetValue("Actor", typeof(IActorRef));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            info.AddValue("Actor", Actor);
            base.GetObjectData(info, context);
        }
#endif

        /// <summary>
        /// Retrieves the actor whose initialization logic failed.
        /// </summary>
        public IActorRef Actor { get; set; }

        /// <summary>
        /// Returns a <see cref="String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            if (Actor == null) return base.ToString();
            return Actor + ": " + base.ToString();
        }
    }

    /// <summary>
    /// This exception is thrown when there was a problem initializing a logger.
    /// </summary>
    public class LoggerInitializationException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerInitializationException"/> class.
        /// </summary>
        public LoggerInitializationException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerInitializationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public LoggerInitializationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerInitializationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public LoggerInitializationException(string message, Exception cause = null)
            : base(message, cause)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerInitializationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected LoggerInitializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when a <see cref="Kill"/> message has been sent to an Actor.
    /// <see cref="SupervisorStrategy.DefaultDecider"/> will by default stop the actor.
    /// </summary>
    public class ActorKilledException : AkkaException
    {
        public ActorKilledException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorKilledException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ActorKilledException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorKilledException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected ActorKilledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when a core invariant in the Actor implementation has been violated.
    /// For instance, if you try to create an Actor that doesn't inherit from <see cref="ActorBase" />.
    /// </summary>
    public class IllegalActorStateException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalActorStateException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public IllegalActorStateException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalActorStateException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected IllegalActorStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when an Actor with an invalid name is deployed.
    /// </summary>
    public class IllegalActorNameException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalActorNameException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public IllegalActorNameException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="IllegalActorNameException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected IllegalActorNameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown by an Actor that receives a Terminated(someActor) message
    /// that it doesn't handle itself, effectively crashing the Actor and escalating to the supervisor.
    /// </summary>
    public class DeathPactException : AkkaException
    {
        private readonly IActorRef _deadActor;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeathPactException"/> class.
        /// </summary>
        /// <param name="deadActor">The actor that has been terminated.</param>
        public DeathPactException(IActorRef deadActor)
            : base("Monitored actor [" + deadActor + "] terminated")
        {
            _deadActor = deadActor;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="DeathPactException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected DeathPactException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

        /// <summary>
        /// Retrieves the actor that has been terminated.
        /// </summary>
        public IActorRef DeadActor
        {
            get { return _deadActor; }
        }
    }

    /// <summary>
    /// This exception is thrown when the <see cref="ActorBase.PreRestart"/> method fails during a restart attempt.
    ///
    /// <note>
    /// This exception is not propagated to the supervisor, as it originates from the already failed instance,
    /// hence it is only visible as log entry on the event stream.
    /// </note>
    /// </summary>
    public class PreRestartException : AkkaException
    {
        private IActorRef Actor;
        private Exception e; //TODO: what is this?
        private Exception exception;
        private object optionalMessage;

        /// <summary>
        /// Initializes a new instance of the <see cref="PreRestartException"/> class.
        /// </summary>
        /// <param name="actor">The actor whose <see cref="ActorBase.PreRestart"/> hook failed.</param>
        /// <param name="restartException">The exception thrown by the <paramref name="actor"/> within <see cref="ActorBase.PreRestart"/>.</param>
        /// <param name="cause">The exception which caused the restart in the first place.</param>
        /// <param name="optionalMessage">The message which was optionally passed into <see cref="ActorBase.PreRestart"/>.</param>
        public PreRestartException(IActorRef actor, Exception restartException, Exception cause, object optionalMessage)
        {
            Actor = actor;
            e = restartException;
            exception = cause;
            this.optionalMessage = optionalMessage;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="PreRestartException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected PreRestartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when the Actor constructor or <see cref="ActorBase.PostRestart"/> method
    /// fails during a restart attempt.
    /// </summary>
    public class PostRestartException : ActorInitializationException
    {
        private readonly Exception _originalCause;

        /// <summary>
        /// Initializes a new instance of the <see cref="PostRestartException"/> class.
        /// </summary>
        /// <param name="actor">The actor whose constructor or <see cref="ActorBase.PostRestart"/> hook failed.</param>
        /// <param name="cause">The exception thrown by the <paramref name="actor"/> within <see cref="ActorBase.PostRestart"/>.</param>
        /// <param name="originalCause">The original cause is the exception which caused the restart in the first place.</param>
        public PostRestartException(IActorRef actor, Exception cause, Exception originalCause)
            :base(actor,"Exception post restart (" + (originalCause == null ?"null" : originalCause.GetType().ToString()) + ")", cause)
        {
            _originalCause = originalCause;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="PostRestartException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected PostRestartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

        ///<summary>
        /// Retrieves the exception which caused the restart in the first place.
        /// </summary>
        public Exception OriginalCause { get { return _originalCause; } }
    }

    /// <summary>
    /// This exception is thrown when an Actor can not be found.
    /// </summary>
    public class ActorNotFoundException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorNotFoundException"/> class.
        /// </summary>
        public ActorNotFoundException()
            : base()
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorNotFoundException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected ActorNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

        /// <summary>
        /// <see cref="ActorNotFoundException"/> that takes a descriptive <paramref name="message"/> and optional <paramref name="innerException"/>.
        /// </summary>
        /// <param name="message">A user-defined error message.</param>
        /// <param name="innerException">An inner <see cref="Exception"/>.</param>
        public ActorNotFoundException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// This exception is thrown when an invalid message is sent to an Actor.
    ///
    /// <note>
    /// Currently only <c>null</c> is an invalid message.
    /// </note>
    /// </summary>
    public class InvalidMessageException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidMessageException"/> class.
        /// </summary>
        public InvalidMessageException()
            : this("Message is null")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidMessageException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public InvalidMessageException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidMessageException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected InvalidMessageException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}

