using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    ///     Class SupervisorStrategy.
    /// </summary>
    public abstract class SupervisorStrategy
    {
        /// <summary>
        ///     The actor failures
        /// </summary>
        protected Dictionary<ActorRef, Failures> actorFailures = new Dictionary<ActorRef, Failures>();

        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="x">The x.</param>
        /// <returns>Directive.</returns>
        public abstract Directive Handle(ActorRef child, Exception x);

        /// <summary>
        ///     This is the main entry point: in case of a child’s failure, this method
        ///     must try to handle the failure by resuming, restarting or stopping the
        ///     child (and returning `true`), or it returns `false` to escalate the
        ///     failure, which will lead to this actor re-throwing the exception which
        ///     caused the failure. The exception will not be wrapped.
        ///     This method calls [[Akka.Actor.SupervisorStrategy#LogFailure]], which will
        ///     log the failure unless it is escalated. You can customize the logging by
        ///     setting [[Akka.Actor.SupervisorStrategy#LoggingEnabled]] to `false` and
        ///     do the logging inside the `decider` or override the `LogFailure` method.
        /// </summary>
        /// <param name="actorCell">The actor cell.</param>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public bool HandleFailure(ActorCell actorCell, ActorRef child, Exception cause)
        {
            Directive directive = Handle(child, cause);
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
                    ProcessFailure(actorCell, true, child, cause);
                    return true;
                case Directive.Stop:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, false, child, cause);
                    return true;
            }
            return false;
        }

        /// <summary>
        ///     When supervisorStrategy is not specified for an actor this
        ///     [[Decider]] is used by default in the supervisor strategy.
        ///     The child will be stopped when [[Akka.Actor.ActorInitializationException]],
        ///     [[Akka.Actor.ActorKilledException]], or [[Akka.Actor.DeathPactException]] is
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
        private void RestartChild(ActorRef child, Exception cause, bool suspendFirst)
        {
            var c = child.AsInstanceOf<InternalActorRef>();
            if (suspendFirst)
                c.Suspend();
            c.AsInstanceOf<InternalActorRef>().Restart(cause);
        }

        /// <summary>
        ///     Processes the failure.
        /// </summary>
        /// <param name="actorCell">The actor cell.</param>
        /// <param name="restart">if set to <c>true</c> [restart].</param>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        private void ProcessFailure(ActorCell actorCell, bool restart, ActorRef child, Exception cause)
        {
            if (restart)
            {
                RestartChild(child, cause, false);
            }
            else
            {
                child.AsInstanceOf<InternalActorRef>().Stop();
            }
            /*
    if (children.nonEmpty) {
      if (restart && children.forall(_.requestRestartPermission(retriesWindow)))
        children foreach (crs ⇒ restartChild(crs.child, cause, suspendFirst = (crs.child != child)))
      else
        for (c ← children) context.stop(c.child)
    }
             */

            //if (children.Any())
            //{
            //    if (restart)
            //    {

            //    }
            //    else
            //    {
            //        foreach (var child in children)
            //        {
            //            child.Stop();
            //        }
            //    }
            //}
        }

        /// <summary>
        ///     Resumes the child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="exception">The exception.</param>
        private void ResumeChild(ActorRef child, Exception exception)
        {
            child.AsInstanceOf<InternalActorRef>().Resume(exception);
        }

        /// <summary>
        ///     Logs the failure.
        /// </summary>
        /// <param name="actorCell">The actor cell.</param>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="directive">The directive.</param>
        protected virtual void LogFailure(ActorCell actorCell, ActorRef child, Exception cause, Directive directive)
        {
            switch (directive)
            {
                case Directive.Resume:
                    actorCell.System.EventStream.Publish(new Warning(child.Path.ToString(), GetType(), cause.Message));
                    break;
                case Directive.Escalate:
                    break;
                default:
                    //case Directive.Restart:
                    //case Directive.Stop:
                    actorCell.System.EventStream.Publish(new Error(cause, child.Path.ToString(), GetType(),
                        cause.Message));
                    break;
            }
        }

        #region Static methods

        /// <summary>
        ///     When supervisorStrategy is not specified for an actor this
        ///     is used by default. OneForOneStrategy with decider defined in
        ///     <see cref="DefaultDecider" />.
        /// </summary>
        /// <value>The default.</value>
        public static SupervisorStrategy DefaultStrategy
        {
            //TODO: should be -1 retries, inf timeout, fix bug that prevents test to pass
            get { return new OneForOneStrategy(10, TimeSpan.FromSeconds(10), DefaultDecider); }
        }

        #endregion
    }

    /// <summary>
    ///     Class OneForOneStrategy. This class cannot be inherited.
    /// </summary>
    public sealed class OneForOneStrategy : SupervisorStrategy
    {
        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to [[akka.actor.OneForOneStrategy]] that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, Duration.Inf means no window.</param>
        /// <param name="decider">mapping from Exception to [[Akka.Actor.SupervisorStrategy.Directive]]</param>
        public OneForOneStrategy(int maxNrOfRetries, TimeSpan withinTimeRange, Func<Exception, Directive> decider)
        {
            MaxNumberOfRetries = maxNrOfRetries;
            WithinTimeRange = withinTimeRange;
            Decider = decider;
        }

        /// <summary>
        ///     The number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </summary>
        /// <value>The maximum number of retries.</value>
        public int MaxNumberOfRetries { get; private set; }

        /// <summary>
        ///     Duration of the time window for maxNrOfRetries, Duration.Inf means no window.
        /// </summary>
        /// <value>The duration.</value>
        public TimeSpan WithinTimeRange { get; private set; }

        /// <summary>
        ///     Mapping from Exception to [[Akka.Actor.SupervisorStrategy.Directive]].
        /// </summary>
        /// <value>The decider.</value>
        public Func<Exception, Directive> Decider { get; private set; }

        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="x">The x.</param>
        /// <returns>Directive.</returns>
        public override Directive Handle(ActorRef child, Exception x)
        {
            Failures failures;
            actorFailures.TryGetValue(child, out failures);
            //create if missing
            if (failures == null)
            {
                failures = new Failures();
                actorFailures.Add(child, failures);
            }
            //add entry
            failures.Entries.Add(new Failure
            {
                Exception = x,
                Timestamp = DateTime.Now,
            });
            //remove expired
            failures.Entries.RemoveAll(f => f.Timestamp < DateTime.Now - WithinTimeRange);
            //calc count of active
            int count = failures.Entries.Count();

            if (count > MaxNumberOfRetries)
            {
                return Directive.Stop;
            }

            Directive whatToDo = Decider(x);
            return whatToDo;
        }
    }

    /// <summary>
    ///     Class AllForOneStrategy. This class cannot be inherited.
    /// </summary>
    public sealed class AllForOneStrategy : SupervisorStrategy
    {
        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="x">The x.</param>
        /// <returns>Directive.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public override Directive Handle(ActorRef child, Exception x)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    ///     Class Failures.
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
    ///     Class Failure.
    /// </summary>
    public class Failure
    {
        /// <summary>
        ///     Gets or sets the exception.
        /// </summary>
        /// <value>The exception.</value>
        public Exception Exception { get; set; }

        /// <summary>
        ///     Gets or sets the timestamp.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    ///     Enum Directive
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
}