﻿//-----------------------------------------------------------------------
// <copyright file="SupervisorStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    ///     Base class for supervision strategies
    /// </summary>
    public abstract class SupervisorStrategy : ISurrogated
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract IDecider Decider { get; }

        /// <summary>
        ///     Handles the specified child.
        /// </summary>
        /// <param name="child">The actor that caused the evaluation to occur</param>
        /// <param name="x">The exception that caused the evaluation to occur.</param>
        /// <returns>Directive.</returns>
        protected abstract Directive Handle(IActorRef child, Exception x);


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorCell">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="failedChildStats">TBD</param>
        /// <param name="allChildren">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete]
        public bool HandleFailure(ActorCell actorCell, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            // for compatibility, since 1.1.2

            return HandleFailure(actorCell, failedChildStats.Child, cause, failedChildStats, allChildren);
        }

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
        /// <param name="child">The child actor.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="stats">The stats for the failed child.</param>
        /// <param name="children">TBD</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public bool HandleFailure(ActorCell actorCell, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children)
        {
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
                    ProcessFailure(actorCell, true, child, cause, stats, children);
                    return true;
                case Directive.Stop:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, false, child, cause, stats, children);
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
        /// <returns>Directive.</returns>
        public static IDecider DefaultDecider = Akka.Actor.Decider.From(Directive.Restart,
            Directive.Stop.When<ActorInitializationException>(),
            Directive.Stop.When<ActorKilledException>(),
            Directive.Stop.When<DeathPactException>());

        /// <summary>
        ///     Restarts the child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="suspendFirst">if set to <c>true</c> [suspend first].</param>
        protected void RestartChild(IActorRef child, Exception cause, bool suspendFirst)
        {
            var c = child.AsInstanceOf<IInternalActorRef>();
            if (suspendFirst)
                c.Suspend();
            c.AsInstanceOf<IInternalActorRef>().Restart(cause);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="restart">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="failedChildStats">TBD</param>
        /// <param name="allChildren">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete]
        // for compatibility, since 1.1.2
        protected abstract void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren);

        /// <summary>
        /// This method is called to act on the failure of a child: restart if the flag is true, stop otherwise.
        /// </summary>
        /// <param name="context">The actor context.</param>
        /// <param name="restart">if set to <c>true</c> restart, stop otherwise.</param>
        /// <param name="child">The child actor</param>
        /// <param name="cause">The exception that caused the child to fail.</param>
        /// <param name="stats">The stats for the child that failed. The ActorRef to the child can be obtained via the <see cref="ChildRestartStats.Child"/> property</param>
        /// <param name="children">The stats for all children</param>
        protected abstract void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children);

        /// <summary>
        ///  Resume the previously failed child: <b>do never apply this to a child which
        ///  is not the currently failing child</b>. Suspend/resume needs to be done in
        ///  matching pairs, otherwise actors will wake up too soon or never at all.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="exception">The exception.</param>
        protected void ResumeChild(IActorRef child, Exception exception)
        {
            child.AsInstanceOf<IInternalActorRef>().Resume(exception);
        }

        /// <summary>
        ///     Logs the failure.
        /// </summary>
        /// <param name="context">The actor cell.</param>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="directive">The directive.</param>
        protected virtual void LogFailure(IActorContext context, IActorRef child, Exception cause, Directive directive)
        {
            if (LoggingEnabled)
            {
                var actorInitializationException = cause as ActorInitializationException;
                string message;
                if (actorInitializationException != null && actorInitializationException.InnerException != null)
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

        /// <summary>
        /// TBD
        /// </summary>
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
        ///     This strategy resembles Erlang in that failing children are always
        ///     terminated (one-for-one).
        /// </summary>
        public static readonly OneForOneStrategy StoppingStrategy = new OneForOneStrategy(ex => Directive.Stop);

        /// <summary>
        /// This method is called after the child has been removed from the set of children.
        /// It does not need to do anything special. Exceptions thrown from this method
        /// do NOT make the actor fail if this happens during termination.
        /// </summary>
        /// <param name="actorContext">TBD</param>
        /// <param name="child">TBD</param>
        /// <param name="children">TBD</param>
        public abstract void HandleChildTerminated(IActorContext actorContext, IActorRef child, IEnumerable<IInternalActorRef> children);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public abstract ISurrogate ToSurrogate(ActorSystem system);
    }

    /// <summary>
    ///     Class OneForOneStrategy. This class cannot be inherited.
    ///  The result of this strategy is applied only to the failing child
    /// </summary>
    public class OneForOneStrategy : SupervisorStrategy, IEquatable<OneForOneStrategy>
    {
        private readonly int _maxNumberOfRetries;
        private readonly int _withinTimeRangeMilliseconds;
        private readonly IDecider _decider;

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxNumberOfRetries
        {
            get { return _maxNumberOfRetries; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int WithinTimeRangeMilliseconds
        {
            get { return _withinTimeRangeMilliseconds; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IDecider Decider
        {
            get { return _decider; }
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     only to the child that failed, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it to all children when one failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, System.Threading.Timeout.InfiniteTimeSpan means no window.</param>
        /// <param name="localOnlyDecider">mapping from Exception to <see cref="Directive" /></param>
        public OneForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, Func<Exception, Directive> localOnlyDecider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), (int)withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).TotalMilliseconds, localOnlyDecider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     only to the child that failed, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it to all children when one failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, System.Threading.Timeout.InfiniteTimeSpan means no window.</param>
        /// <param name="decider">mapping from Exception to <see cref="Directive" /></param>
        public OneForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, IDecider decider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), (int)withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).TotalMilliseconds, decider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     only to the child that failed, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it to all children when one failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeMilliseconds">duration in milliseconds of the time window for <paramref name="maxNrOfRetries"/>, negative values means no window.</param>
        /// <param name="localOnlyDecider">Mapping from an <see cref="Exception"/> to <see cref="Directive"/></param>
        /// <param name="loggingEnabled">If <c>true</c> failures will be logged</param>
        public OneForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, Func<Exception, Directive> localOnlyDecider, bool loggingEnabled = true)
            : this(maxNrOfRetries, withinTimeMilliseconds, new LocalOnlyDecider(localOnlyDecider), loggingEnabled)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     only to the child that failed, as opposed to <see cref="AllForOneStrategy" /> that applies
        ///     it to all children when one failed.
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
            _maxNumberOfRetries = maxNrOfRetries;
            _withinTimeRangeMilliseconds = withinTimeMilliseconds;
            _decider = decider;
            LoggingEnabled = loggingEnabled;
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        /// <param name="localOnlyDecider">TBD</param>
        public OneForOneStrategy(Func<Exception, Directive> localOnlyDecider) : this(-1, -1, localOnlyDecider, true)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        /// <param name="decider">TBD</param>
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
        protected override Directive Handle(IActorRef child, Exception x)
        {
            return Decider.Decide(x);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="restart">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="failedChildStats">TBD</param>
        /// <param name="allChildren">TBD</param>
        [Obsolete]
        protected override void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            // for compatibility, since 1.1.2

            ProcessFailure(context, restart, failedChildStats.Child, cause, failedChildStats, allChildren);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="restart">TBD</param>
        /// <param name="child">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="stats">TBD</param>
        /// <param name="children">TBD</param>
        protected override void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children)
        {
            if (restart && stats.RequestRestartPermission(MaxNumberOfRetries, WithinTimeRangeMilliseconds))
                RestartChild(child, cause, suspendFirst: false);
            else
                context.Stop(child);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorContext">TBD</param>
        /// <param name="child">TBD</param>
        /// <param name="children">TBD</param>
        public override void HandleChildTerminated(IActorContext actorContext, IActorRef child, IEnumerable<IInternalActorRef> children)
        {
            //Intentionally left blank
        }

        #region Surrogate		
        /// <summary>
        /// TBD
        /// </summary>
        public class OneForOneStrategySurrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            public int MaxNumberOfRetries { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int WithinTimeRangeMilliseconds { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IDecider Decider { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool LoggingEnabled { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new OneForOneStrategy(MaxNumberOfRetries, WithinTimeRangeMilliseconds, Decider, LoggingEnabled);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="NotSupportedException">This exception is thrown if the <see cref="Decider"/> is of type <see cref="LocalOnlyDecider"/>.</exception>
        /// <returns>TBD</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            if (Decider is LocalOnlyDecider)
                throw new NotSupportedException("Can not serialize LocalOnlyDecider");
            return new OneForOneStrategySurrogate
            {
                Decider = Decider,
                LoggingEnabled = LoggingEnabled,
                MaxNumberOfRetries = MaxNumberOfRetries,
                WithinTimeRangeMilliseconds = WithinTimeRangeMilliseconds
            };
        }
        #endregion

        #region Equals
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(OneForOneStrategy other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return MaxNumberOfRetries.Equals(other.MaxNumberOfRetries) &&
                   WithinTimeRangeMilliseconds.Equals(other.WithinTimeRangeMilliseconds) &&
                   Decider.Equals(other.Decider);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as OneForOneStrategy);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Decider != null ? Decider.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ MaxNumberOfRetries.GetHashCode();
                hashCode = (hashCode * 397) ^ WithinTimeRangeMilliseconds.GetHashCode();
                return hashCode;
            }
        }
        #endregion
    }

    /// <summary>
    ///     Class AllForOneStrategy. This class cannot be inherited.
    /// The result of this strategy is applied to the failed child and all its siblings.
    /// </summary>
    public class AllForOneStrategy : SupervisorStrategy, IEquatable<AllForOneStrategy>
    {
        private readonly IDecider _decider;
        private readonly int _withinTimeRangeMilliseconds;
        private readonly int _maxNumberOfRetries;

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxNumberOfRetries
        {
            get { return _maxNumberOfRetries; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int WithinTimeRangeMilliseconds
        {
            get { return _withinTimeRangeMilliseconds; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IDecider Decider
        {
            get { return _decider; }
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="OneForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value and null means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, <see cref="Timeout.InfiniteTimeSpan"/> means no window.</param>
        /// <param name="localOnlyDecider">mapping from Exception to <see cref="Directive"/></param>
        public AllForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, Func<Exception, Directive> localOnlyDecider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), (int)withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).TotalMilliseconds, localOnlyDecider)
        {
            //Intentionally left blank
        }

        /// <summary>
        ///     Applies the fault handling `Directive` (Resume, Restart, Stop) specified in the `Decider`
        ///     to all children when one fails, as opposed to <see cref="OneForOneStrategy" /> that applies
        ///     it only to the child actor that failed.
        /// </summary>
        /// <param name="maxNrOfRetries">
        ///     the number of times a child actor is allowed to be restarted, negative value and null means no limit,
        ///     if the limit is exceeded the child actor is stopped.
        /// </param>
        /// <param name="withinTimeRange">duration of the time window for maxNrOfRetries, <see cref="Timeout.InfiniteTimeSpan"/> means no window.</param>
        /// <param name="decider">mapping from Exception to <see cref="Directive"/></param>
        public AllForOneStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, IDecider decider)
            : this(maxNrOfRetries.GetValueOrDefault(-1), (int)withinTimeRange.GetValueOrDefault(Timeout.InfiniteTimeSpan).TotalMilliseconds, decider)
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
        public AllForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, Func<Exception, Directive> localOnlyDecider, bool loggingEnabled = true)
            : this(maxNrOfRetries, withinTimeMilliseconds, new LocalOnlyDecider(localOnlyDecider), loggingEnabled)
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
        public AllForOneStrategy(int maxNrOfRetries, int withinTimeMilliseconds, IDecider decider, bool loggingEnabled = true)
        {
            _maxNumberOfRetries = maxNrOfRetries;
            _withinTimeRangeMilliseconds = withinTimeMilliseconds;
            _decider = decider;
            LoggingEnabled = loggingEnabled;
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        /// <param name="localOnlyDecider">TBD</param>
        public AllForOneStrategy(Func<Exception, Directive> localOnlyDecider)
            : this(-1, -1, localOnlyDecider, true)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Constructor that accepts only a decider and uses reasonable defaults for the other settings
        /// </summary>
        /// <param name="decider">TBD</param>
        public AllForOneStrategy(IDecider decider)
            : this(-1, -1, decider, true)
        {
            //Intentionally left blank
        }


        /// <summary>
        /// Serialization-friendly constructor
        /// </summary>
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
        protected override Directive Handle(IActorRef child, Exception x)
        {
            return Decider.Decide(x);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="restart">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="failedChildStats">TBD</param>
        /// <param name="allChildren">TBD</param>
        [Obsolete]
        protected override void ProcessFailure(IActorContext context, bool restart, Exception cause, ChildRestartStats failedChildStats, IReadOnlyCollection<ChildRestartStats> allChildren)
        {
            // for compatibility, since 1.1.2

            ProcessFailure(context, restart, failedChildStats.Child, cause, failedChildStats, allChildren);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="restart">TBD</param>
        /// <param name="child">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="stats">TBD</param>
        /// <param name="children">TBD</param>
        protected override void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children)
        {
            if (children.Count > 0)
            {
                if (restart && children.All(c => c.RequestRestartPermission(MaxNumberOfRetries, WithinTimeRangeMilliseconds)))
                {
                    foreach (var crs in children)
                    {
                        RestartChild(crs.Child, cause, suspendFirst: !child.Equals(crs.Child));
                    }
                }
                else
                {
                    foreach (var crs in children)
                    {
                        context.Stop(crs.Child);
                    }
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorContext">TBD</param>
        /// <param name="child">TBD</param>
        /// <param name="children">TBD</param>
        public override void HandleChildTerminated(IActorContext actorContext, IActorRef child, IEnumerable<IInternalActorRef> children)
        {
            //Intentionally left blank
        }

        #region Surrogate		
        /// <summary>
        /// TBD
        /// </summary>
        public class AllForOneStrategySurrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            public int MaxNumberOfRetries { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int WithinTimeRangeMilliseconds { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IDecider Decider { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool LoggingEnabled { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new AllForOneStrategy(MaxNumberOfRetries, WithinTimeRangeMilliseconds, Decider, LoggingEnabled);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new AllForOneStrategySurrogate
            {
                Decider = Decider,
                LoggingEnabled = LoggingEnabled,
                MaxNumberOfRetries = MaxNumberOfRetries,
                WithinTimeRangeMilliseconds = WithinTimeRangeMilliseconds
            };
        }
        #endregion

        #region Equals
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(AllForOneStrategy other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return MaxNumberOfRetries.Equals(other.MaxNumberOfRetries) &&
                   WithinTimeRangeMilliseconds.Equals(other.WithinTimeRangeMilliseconds) &&
                   Decider.Equals(other.Decider);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as AllForOneStrategy);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Decider != null ? Decider.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ MaxNumberOfRetries.GetHashCode();
                hashCode = (hashCode * 397) ^ WithinTimeRangeMilliseconds.GetHashCode();
                return hashCode;
            }
        }
        #endregion
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

    /// <summary>
    /// TBD
    /// </summary>
    public static class DirectiveExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TException">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static KeyValuePair<Type, Directive> When<TException>(this Directive self) where TException : Exception
        {
            return new KeyValuePair<Type, Directive>(typeof(TException), self);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IDecider
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        Directive Decide(Exception cause);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Decider
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultDirective">TBD</param>
        /// <param name="pairs">TBD</param>
        /// <returns>TBD</returns>
        public static DeployableDecider From(Directive defaultDirective, params KeyValuePair<Type, Directive>[] pairs)
        {
            return new DeployableDecider(defaultDirective, pairs);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultDirective">TBD</param>
        /// <param name="pairs">TBD</param>
        /// <returns>TBD</returns>
        public static DeployableDecider From(Directive defaultDirective, IEnumerable<KeyValuePair<Type, Directive>> pairs)
        {
            return new DeployableDecider(defaultDirective, pairs);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localOnlyDecider">TBD</param>
        /// <returns>TBD</returns>
        public static LocalOnlyDecider From(Func<Exception, Directive> localOnlyDecider)
        {
            return new LocalOnlyDecider(localOnlyDecider);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class LocalOnlyDecider : IDecider
    {
        private readonly Func<Exception, Directive> _decider;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="decider">TBD</param>
        public LocalOnlyDecider(Func<Exception, Directive> decider)
        {
            _decider = decider;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        public Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class DeployableDecider : IDecider, IEquatable<DeployableDecider>
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected DeployableDecider()
        {
            //Json .net can not decide which of the other ctors are the correct one to use
            //so we fall back to default ctor and property injection for deserializer
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultDirective">TBD</param>
        /// <param name="pairs">TBD</param>
        public DeployableDecider(Directive defaultDirective, IEnumerable<KeyValuePair<Type, Directive>> pairs) : this(defaultDirective, pairs.ToArray())
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultDirective">TBD</param>
        /// <param name="pairs">TBD</param>
        public DeployableDecider(Directive defaultDirective, params KeyValuePair<Type, Directive>[] pairs)
        {
            DefaultDirective = defaultDirective;
            Pairs = pairs;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Directive DefaultDirective { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public KeyValuePair<Type, Directive>[] Pairs { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(DeployableDecider other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return DefaultDirective.Equals(other.DefaultDirective) &&
                   Pairs.SequenceEqual(other.Pairs);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeployableDecider);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Pairs != null ? Pairs.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ((int)DefaultDirective).GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class SupervisorStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract SupervisorStrategy Create();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the given <paramref name="typeName"/> is undefined or references an unknown type.
        /// </exception>
        /// <returns>TBD</returns>
        public static SupervisorStrategyConfigurator CreateConfigurator(string typeName)
        {
            switch (typeName)
            {
                case "Akka.Actor.DefaultSupervisorStrategy":
                    return new DefaultSupervisorStrategy();

                case "Akka.Actor.StoppingSupervisorStrategy":
                    return new StoppingSupervisorStrategy();

                case null:
                    throw new ConfigurationException("Could not resolve SupervisorStrategyConfigurator. typeName is null");

                default:
                    Type configuratorType = Type.GetType(typeName);

                    if (configuratorType == null)
                        throw new ConfigurationException($"Could not resolve SupervisorStrategyConfigurator type {typeName}");

                    return (SupervisorStrategyConfigurator)Activator.CreateInstance(configuratorType);
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class DefaultSupervisorStrategy : SupervisorStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override SupervisorStrategy Create()
        {
            return SupervisorStrategy.DefaultStrategy;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class StoppingSupervisorStrategy : SupervisorStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override SupervisorStrategy Create()
        {
            return SupervisorStrategy.StoppingStrategy;
        }
    }
}
