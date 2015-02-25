using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    ///     Base class for supervision strategies
    /// </summary>
    public abstract class SupervisorStrategy
    {
        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The actor that caused the evaluation to occur</param>
        /// <param name="x">The exception that caused the evaluation to occur.</param>
        /// <returns>Directive.</returns>
        protected abstract Directive Handle(ActorRef child, Exception x);

        /// <summary>
        ///     This is the main entry point: in case of a child’s failure, this method
        ///     must try to handle the failure by resuming, restarting or stopping the
        ///     child (and returning `true`), or it returns `false` to escalate the
        ///     failure, which will lead to this actor re-throwing the exception which
        ///     caused the failure. The exception will not be wrapped.
        ///     This method calls <see cref="Akka.Actor.SupervisorStrategy"/>, which will
        ///     log the failure unless it is escalated. You can customize the logging by
        ///     setting <see cref="Akka.Actor.SupervisorStrategy" /> to `false` and
        ///     do the logging inside the `decider` or override the `LogFailure` method.
        /// </summary>
        /// <param name="actorCell">The actor cell.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="failedChildStats">The stats for the failed child.</param>
        /// <param name="allChildren"></param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public bool HandleFailure(ActorCell actorCell, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            var child = failedChildStats.Child;
            var directive = Handle(child, cause);
            switch (directive)
            {
                case Directive.Escalate:
                    LogFailure(actorCell, child, cause, directive);
                    return false;
                case Directive.Resume:
                    LogFailure(actorCell, child, cause, directive);
                    ResumeChild(child, cause);
                    return true;
                case Directive.Restart:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, true, cause, failedChildStats, allChildren);
                    return true;
                case Directive.Stop:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, false, cause, failedChildStats, allChildren);
                    return true;
            }
            return false;
        }

        /// <summary>
        ///     When supervisorStrategy is not specified for an actor this
        ///     Decider is used by default in the supervisor strategy.
        ///     The child will be stopped when <see cref="Akka.Actor.ActorInitializationException"/>,
        ///     <see cref="Akka.Actor.ActorKilledException"/>, or <see cref="Akka.Actor.DeathPactException"/> is
        ///     thrown. It will be restarted for other `Exception` types.
        ///     The error is escalated if it's a `Exception`, i.e. `Error`.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>Directive.</returns>
        public static Directive DefaultDecider(Exception exception)
        {
            if (exception is ActorInitializationException)
                return Directive.Stop;
            if (exception is ActorKilledException)
                return Directive.Stop;
            if (exception is DeathPactException)
                return Directive.Stop;

            return Directive.Restart;
        }

        /// <summary>
        ///     Restarts the child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="suspendFirst">if set to <c>true</c> [suspend first].</param>
        protected void RestartChild(ActorRef child, Exception cause, bool suspendFirst)
        {
            var c = child.AsInstanceOf<InternalActorRef>();
            if (suspendFirst)
                c.Suspend();
            c.AsInstanceOf<InternalActorRef>().Restart(cause);
        }

        /// <summary>
        /// This method is called to act on the failure of a child: restart if the flag is true, stop otherwise.
        /// </summary>
        /// <param name="context">The actor context.</param>
        /// <param name="restart">if set to <c>true</c> restart, stop otherwise.</param>
        /// <param name="cause">The exception that caused the child to fail.</param>
        /// <param name="failedChildStats">The stats for the child that failed. The ActorRef to the child can be obtained via the <see cref="ChildRestartStats.Child"/> property</param>
        /// <param name="allChildren">The stats for all children</param>
        protected abstract void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren);

        /// <summary>
        ///  Resume the previously failed child: <b>do never apply this to a child which
        ///  is not the currently failing child</b>. Suspend/resume needs to be done in
        ///  matching pairs, otherwise actors will wake up too soon or never at all.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="exception">The exception.</param>
        protected void ResumeChild(ActorRef child, Exception exception)
        {
            child.AsInstanceOf<InternalActorRef>().Resume(exception);
        }

        /// <summary>
        ///     Logs the failure.
        /// </summary>
        /// <param name="context">The actor cell.</param>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="directive">The directive.</param>
        protected virtual void LogFailure(IActorContext context, ActorRef child, Exception cause, Directive directive)
        {
            if(LoggingEnabled)
            {
                var actorInitializationException = cause as ActorInitializationException;
                string message;
                if(actorInitializationException != null && actorInitializationException.InnerException != null)
                    message = actorInitializationException.InnerException.Message;
                else
                    message = cause.Message;
                switch (directive)
                {
                    case Directive.Resume:
                        Publish(context, new Warning(child.Path.ToString(), GetType(), message));
                        break;
                    case Directive.Escalate:
                        //Don't log here
                        break;
                    default:
                        //case Directive.Restart:
                        //case Directive.Stop:
                        Publish(context, new Error(cause, child.Path.ToString(), GetType(), message));
                        break;
                }
            }
        }

        protected bool LoggingEnabled { get; set; }

        private void Publish(IActorContext context, LogEvent logEvent)
        {
            try
            {
                context.System.EventStream.Publish(logEvent);
            }
            catch (Exception)
            {
               // swallow any exceptions
            }
        }

        /// <summary>
        ///     When supervisorStrategy is not specified for an actor this
        ///     is used by default. OneForOneStrategy with decider defined in
        ///     <see cref="DefaultDecider" />.
        /// </summary>
        /// <value>The default.</value>
        public static readonly SupervisorStrategy DefaultStrategy = new OneForOneStrategy(DefaultDecider);    

        /// <summary>
        /// This method is called after the child has been removed from the set of children.
        /// It does not need to do anything special. Exceptions thrown from this method
        /// do NOT make the actor fail if this happens during termination.
        /// </summary>
        public abstract void HandleChildTerminated(IActorContext actorContext, ActorRef child, IEnumerable<InternalActorRef> children);

    }

    /// <summary>
    ///     Class OneForOneStrategy. This class cannot be inherited.
    /// </summary>
    public class OneForOneStrategy : SupervisorStrategy
    {
        public int MaxNumberOfRetries { get; private set; }
        public int WithinTimeRangeMilliseconds { get; private set; }
        public IDecider Decider { get; private set; }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="OneForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, Duration.Inf means no window.</param>
        /// <param name="localOnlyDecider">mapping from Exception to <see cref="Directive" /></param>
        public OneForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, Func<Exception, Directive> localOnlyDecider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).Milliseconds, localOnlyDecider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="OneForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeMilliseconds">duration in milliseconds of the time window for <paramref name="maxNrOfRetries"/>, negative values means no window.</param>
        /// <param name="localOnlyDecider">Mapping from an <see cref="Exception"/> to <see cref="Directive"/></param>
        /// <param name="loggingEnabled">If <c>true</c> failures will be logged</param>
        public OneForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, Func<Exception, Directive> localOnlyDecider, bool loggingEnabled = true) : this(maxNrOfRetries,withinTimeMilliseconds,new LocalOnlyDecider(localOnlyDecider),loggingEnabled)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="OneForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeMilliseconds">duration in milliseconds of the time window for <paramref name="maxNrOfRetries"/>, negative values means no window.</param>
        /// <param name="decider">Mapping from an <see cref="Exception"/> to <see cref="Directive"/></param>
        /// <param name="loggingEnabled">If <c>true</c> failures will be logged</param>
        public OneForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, IDecider decider, bool loggingEnabled = true)
        {
            MaxNumberOfRetries = maxNrOfRetries;
            WithinTimeRangeMilliseconds = withinTimeMilliseconds;
            Decider = decider;
            LoggingEnabled = loggingEnabled;
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        public OneForOneStrategy(Func<Exception, Directive> localOnlyDecider) : this(-1, -1, localOnlyDecider, true)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        public OneForOneStrategy(IDecider decider)
            : this(-1, -1, decider, true)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Serialization-friendly constructor
        /// </summary>
        protected OneForOneStrategy() : this(DefaultDecider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="x">The x.</param>
        /// <returns>Directive.</returns>
        protected override Directive Handle(ActorRef child, Exception x)
        {
            return Decider.Decide(x);
        }

        protected override void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            var failedChild = failedChildStats.Child;

            if (restart && failedChildStats.RequestRestartPermission(MaxNumberOfRetries, WithinTimeRangeMilliseconds))
                RestartChild(failedChild, cause, suspendFirst: false);
            else
                context.Stop(failedChild);
        }


        public override void HandleChildTerminated(IActorContext actorContext, ActorRef child, IEnumerable<InternalActorRef> children)
        {
            //Intentionally left blank
        }
    }

    /// <summary>
    ///     Class AllForOneStrategy. This class cannot be inherited.
    /// </summary>
    public class AllForOneStrategy : SupervisorStrategy
    {
        public int MaxNumberOfRetries { get; private set; }
        public int WithinTimeRangeMilliseconds { get; private set; }
        public IDecider Decider { get; private set; }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value and null means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, <see cref="Timeout.InfiniteTimeSpan"/> means no window.</param>
        /// <param name="localOnlyDecider">mapping from Exception to <see cref="Directive"/></param>
        public AllForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, Func<Exception, Directive> localOnlyDecider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).Milliseconds, localOnlyDecider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value and null means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, <see cref="Timeout.InfiniteTimeSpan"/> means no window.</param>
        /// <param name="decider">mapping from Exception to <see cref="Directive"/></param>
        public AllForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, IDecider decider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).Milliseconds, decider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeMilliseconds">duration in milliseconds of the time window for <paramref name="maxNrOfRetries"/>, negative values means no window.</param>
        /// <param name="localOnlyDecider">Mapping from an <see cref="Exception"/> to <see cref="Directive"/></param>
        /// <param name="loggingEnabled">If <c>true</c> failures will be logged</param>
        public AllForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, Func<Exception, Directive> localOnlyDecider, bool loggingEnabled=true) : this(maxNrOfRetries,withinTimeMilliseconds,new LocalOnlyDecider(localOnlyDecider),loggingEnabled)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeMilliseconds">duration in milliseconds of the time window for <paramref name="maxNrOfRetries"/>, negative values means no window.</param>
        /// <param name="decider">Mapping from an <see cref="Exception"/> to <see cref="Directive"/></param>
        /// <param name="loggingEnabled">If <c>true</c> failures will be logged</param>
        public AllForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, IDecider decider, bool loggingEnabled = true)
        {
            MaxNumberOfRetries = maxNrOfRetries;
            WithinTimeRangeMilliseconds = withinTimeMilliseconds;
            Decider = decider;
            LoggingEnabled = loggingEnabled;
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        public AllForOneStrategy(Func<Exception, Directive> localOnlyDecider)
            : this(-1, -1, localOnlyDecider, true)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        public AllForOneStrategy(IDecider decider)
            : this(-1, -1, decider, true)
        {
            //Intentionally left blank
        }


        /// <summary>
        /// Serialization-friendly constructor
        /// </summary>]
        protected AllForOneStrategy() : this(DefaultDecider)
        {
            //Intentionally left blank
        }
  
        /// <summary>
        ///     Determines what to do with the child when the given exception occurs.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="x">The x.</param>
        /// <returns>Directive.</returns>
        protected override Directive Handle(ActorRef child, Exception x)
        {
            return Decider.Decide(x);
        }

        protected override void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            if (allChildren.Count > 0)
            {
                var failedChild = failedChildStats.Child;

                if (restart && allChildren.All(c => c.RequestRestartPermission(MaxNumberOfRetries, WithinTimeRangeMilliseconds)))
                {
                    foreach (var crs in allChildren)
                    {
                        RestartChild(crs.Child, cause, suspendFirst: !failedChild.Equals(crs.Child));
                    }
                }
                else
                {
                    foreach (var crs in allChildren)
                    {
                        context.Stop(crs.Child);
                    }
                }
            }
        }

        public override void HandleChildTerminated(IActorContext actorContext, ActorRef child, IEnumerable<InternalActorRef> children)
        {
            //Intentionally left blank
        }
    }

    /// <summary>
    ///     Collection of failures, used to keep track of how many times a given actor have failed.
    /// </summary>
    public class Failures
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Failures" /> class.
        /// </summary>
        public Failures()
        {
            Entries = new List<Failure>();
        }

        /// <summary>
        ///     Gets the entries.
        /// </summary>
        /// <value>The entries.</value>
        public List<Failure> Entries { get; private set; }
    }

    /// <summary>
    ///     Represents a single failure.
    /// </summary>
    public class Failure
    {
        /// <summary>
        ///     The exception that caused the failure.
        /// </summary>
        /// <value>The exception.</value>
        public Exception Exception { get; set; }

        /// <summary>
        ///     The timestamp when the failure occurred.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    ///     Enum Directive for supervisor actions
    /// </summary>
    public enum Directive
    {
        /// <summary>
        ///     Resumes message processing for the failed Actor
        /// </summary>
        Resume,

        /// <summary>
        ///     Discards the old Actor instance and replaces it with a new,
        ///     then resumes message processing.
        /// </summary>
        Restart,

        /// <summary>
        ///     Escalates the failure to the supervisor of the supervisor,
        ///     by rethrowing the cause of the failure, i.e. the supervisor fails with
        ///     the same exception as the child.
        /// </summary>
        Escalate,

        /// <summary>
        ///     Stops the Actor
        /// </summary>
        Stop,
    }

    public static class DirectiveExtensions
    {
        public static KeyValuePair<Type, Directive> When<TException>(this Directive self) where TException : Exception
        {
            return new KeyValuePair<Type, Directive>(typeof(TException),self);
        }
    }

    public interface IDecider
    {
        Directive Decide(Exception cause);        
    }

    public static class Decider
    {
        public static DeployableDecider From(Directive defaultDirective, params KeyValuePair<Type, Directive>[] pairs)
        {
            return new DeployableDecider(defaultDirective, pairs);     
        }

        public static DeployableDecider From(Directive defaultDirective, IEnumerable<KeyValuePair<Type, Directive>> pairs)
        {
            return new DeployableDecider(defaultDirective, pairs);
        }

        public static LocalOnlyDecider From(Func<Exception, Directive> localOnlyDecider)
        {
            return new LocalOnlyDecider(localOnlyDecider);
        }
    }

    public class LocalOnlyDecider : IDecider
    {
        private readonly Func<Exception, Directive> _decider;
        public LocalOnlyDecider(Func<Exception, Directive> decider)
        {
            _decider = decider;
        }

        public Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    public class DeployableDecider : IDecider
    {
        //Json .net can not decide which of the other ctors are the correct one to use
        //so we fall back to default ctor and property injection for deserializer
        protected DeployableDecider()
        {            
        }

        public DeployableDecider(Directive defaultDirective, IEnumerable<KeyValuePair<Type, Directive>> pairs) : this(defaultDirective,pairs.ToArray())
        {
        }

        public DeployableDecider(Directive defaultDirective,params KeyValuePair<Type, Directive>[] pairs)
        {
            DefaultDirective = defaultDirective;
            Pairs = pairs;
        }

        public Directive DefaultDirective { get; private set; }

        public KeyValuePair<Type, Directive>[] Pairs { get; private set; }

        public Directive Decide(Exception cause)
        {
            if (Pairs != null)
            {
                foreach (var kvp in Pairs)
                {
                    //emulate if (cause is SomeType)
                    if (kvp.Key.IsInstanceOfType(cause))
                    {
                        return kvp.Value;
                    }
                }
            }

            return DefaultDirective;
        }
    }
}