//-----------------------------------------------------------------------
// <copyright file="DeadLetterListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an actor responsible for listening to <see cref="DeadLetter"/> messages and logging them using the <see cref="EventStream"/>.
    /// </summary>
    public class DeadLetterListener : ActorBase
    {
        private readonly EventStream _eventStream = Context.System.EventStream;
        private readonly bool _isAlwaysLoggingDeadLetters = Context.System.Settings.LogDeadLetters == int.MaxValue;
        private readonly int _maxCount = Context.System.Settings.LogDeadLetters;
        private int _count;

        /// <summary>
        /// Don't re-subscribe, skip call to preStart
        /// </summary>
        protected override void PostRestart(Exception reason)
        {
        }

        /// <summary>
        /// Don't remove subscription, skip call to postStop, no children to stop
        /// </summary>
        protected override void PreRestart(Exception reason, object message)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            _eventStream.Subscribe(Self, typeof(DeadLetter));
            _eventStream.Subscribe(Self, typeof(Dropped));
            _eventStream.Subscribe(Self, typeof(UnhandledMessage));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _eventStream.Unsubscribe(Self);
        }

        private void IncrementCount()
        {
            if (_count == int.MaxValue)
            {
                Logging.GetLogger(Context.System, this).Info("Resetting DeadLetterListener counter after reaching Int.MaxValue.");
                _count = 1;
            }
            else
            {
                _count++;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (_isAlwaysLoggingDeadLetters)
            {
                return ReceiveWithAlwaysLogging()(message);
            }

            return Context.System.Settings.LogDeadLettersSuspendDuration != Timeout.InfiniteTimeSpan
                ? ReceiveWithSuspendLogging(Context.System.Settings.LogDeadLettersSuspendDuration)(message)
                : ReceiveWithMaxCountLogging()(message);
        }

        private Receive ReceiveWithAlwaysLogging()
        {
            return message =>
            {
                if (message is AllDeadLetters d)
                {
                    if (!IsWrappedSuppressed(d))
                    {
                        IncrementCount();
                        LogDeadLetter(d, "");
                    }
                    return true;
                }
                return false;
            };
        }

        private Receive ReceiveWithMaxCountLogging()
        {
            return message =>
            {
                if (message is AllDeadLetters d)
                {
                    if (!IsWrappedSuppressed(d))
                    {
                        IncrementCount();
                        if (_count == _maxCount)
                        {
                            LogDeadLetter(d, ", no more dead letters will be logged");
                            Context.Stop(Self);
                        }
                        else
                        {
                            LogDeadLetter(d, "");
                        }
                    }
                    return true;
                }
                return false;
            };
        }

        private Receive ReceiveWithSuspendLogging(TimeSpan suspendDuration)
        {
            return message =>
            {
                if (message is AllDeadLetters d)
                {
                    if (!IsWrappedSuppressed(d))
                    {
                        IncrementCount();
                        if (_count == _maxCount)
                        {
                            var doneMsg = $", no more dead letters will be logged in next [{suspendDuration}]";
                            LogDeadLetter(d, doneMsg);
                            Context.Become(ReceiveWhenSuspended(suspendDuration, Deadline.Now + suspendDuration));
                        }
                        else
                        {
                            LogDeadLetter(d, "");
                        }
                    }
                    return true;
                }
                return false;
            };
        }

        private Receive ReceiveWhenSuspended(TimeSpan suspendDuration, Deadline suspendDeadline)
        {
            return message =>
            {
                if (message is AllDeadLetters d)
                {
                    if (!IsWrappedSuppressed(d))
                    {
                        IncrementCount();
                        if (suspendDeadline.IsOverdue)
                        {
                            var doneMsg = $", of which {(_count - _maxCount - 1)} were not logged. The counter will be reset now";
                            LogDeadLetter(d, doneMsg);
                            _count = 0;
                            Context.Become(ReceiveWithSuspendLogging(suspendDuration));
                        }
                    }
                    return true;
                }
                return false;
            };
        }

        private void LogDeadLetter(AllDeadLetters d, string doneMsg)
        {
            var origin = IsReal(d.Sender) ? $" from {d.Sender}" : "";
            var unwrapped = WrappedMessage.Unwrap(d.Message);
            var messageStr = unwrapped?.GetType().Name ?? "null";
            var wrappedIn = (d.Message is IWrappedMessage) ? $" wrapped in [${d.Message.GetType().Name}]" : "";

            string logMessage;
            switch (d)
            {
                case Dropped dropped:
                    var destination = IsReal(d.Recipient) ? $" to {d.Recipient}" : "";
                    logMessage = $"Message [{messageStr}]{wrappedIn}{origin}{destination} was dropped. {dropped.Reason}. " +
                    $"[{_count}] dead letters encountered{doneMsg}. ";
                    break;
                case UnhandledMessage unhandled:
                    destination = IsReal(d.Recipient) ? $" to {d.Recipient}" : "";
                    logMessage = $"Message [{messageStr}]{wrappedIn}{origin}{destination} was unhandled. " +
                    $"[{_count}] dead letters encountered{doneMsg}. ";
                    break;
                default:
                    logMessage = $"Message [{messageStr}]{wrappedIn}{origin} to {d.Recipient} was not delivered. " +
                    $"[{_count}] dead letters encountered{doneMsg}. " +
                    $"If this is not an expected behavior then {d.Recipient} may have terminated unexpectedly. ";
                    break;
            }
            _eventStream.Publish(new Info(
                d.Recipient.Path.ToString(),
                d.Recipient.GetType(),
                logMessage +
                "This logging can be turned off or adjusted with configuration settings 'akka.log-dead-letters' " +
                "and 'akka.log-dead-letters-during-shutdown'."));
        }

        private bool IsReal(IActorRef snd)
        {
            return !ReferenceEquals(snd, ActorRefs.NoSender) && !ReferenceEquals(snd, Context.System.DeadLetters) && !(snd is DeadLetterActorRef);
        }

        private bool IsWrappedSuppressed(AllDeadLetters d)
        {
            return d is IWrappedMessage w && w.Message is IDeadLetterSuppression;
        }

        /// <summary>
        /// This class represents the latest date or time by which an operation should be completed.
        /// </summary>
        private readonly struct Deadline : IEquatable<Deadline>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Deadline"/> class.
            /// </summary>
            /// <param name="when">The <see cref="DateTime"/> that the deadline is due.</param>
            private Deadline(DateTime when) => When = when;

            /// <summary>
            /// Determines whether the deadline has past.
            /// </summary>
            public bool IsOverdue => DateTime.UtcNow > When;

            /// <summary>
            /// Determines whether there is still time left until the deadline.
            /// </summary>
            public bool HasTimeLeft => DateTime.UtcNow < When;

            /// <summary>
            /// The <see cref="DateTime"/> that the deadline is due.
            /// </summary>
            public DateTime When { get; }

            /// <summary>
            /// <para>
            /// The amount of time left until the deadline is reached.
            /// </para>
            /// <note>
            /// Warning: creates a new <see cref="TimeSpan"/> instance each time it's used
            /// </note>
            /// </summary>
            public TimeSpan TimeLeft { get { return When - DateTime.UtcNow; } }

            #region Overrides

            /// <inheritdoc/>
            public override bool Equals(object obj) =>
                obj is Deadline deadline && Equals(deadline);

            /// <inheritdoc/>
            public bool Equals(Deadline other) => When == other.When;

            /// <inheritdoc/>
            public override int GetHashCode() => When.GetHashCode();

            #endregion

            #region Static members

            /// <summary>
            /// A deadline that is due <see cref="DateTime.UtcNow"/>
            /// </summary>
            public static Deadline Now => new Deadline(DateTime.UtcNow);

            /// <summary>
            /// Adds a given <see cref="TimeSpan"/> to the due time of this <see cref="Deadline"/>
            /// </summary>
            /// <param name="deadline">The deadline whose time is being extended</param>
            /// <param name="duration">The amount of time being added to the deadline</param>
            /// <returns>A new deadline with the specified duration added to the due time</returns>
            public static Deadline operator +(Deadline deadline, TimeSpan duration)
            {
                return new Deadline(deadline.When.Add(duration));
            }

            /// <summary>
            /// Adds a given <see cref="Nullable{TimeSpan}"/> to the due time of this <see cref="Deadline"/>
            /// </summary>
            /// <param name="deadline">The deadline whose time is being extended</param>
            /// <param name="duration">The amount of time being added to the deadline</param>
            /// <returns>A new deadline with the specified duration added to the due time</returns>
            public static Deadline operator +(Deadline deadline, TimeSpan? duration)
            {
                return duration.HasValue ? new Deadline(deadline.When.Add(duration.Value)) : deadline;
            }

            #endregion
        }
    }
}

