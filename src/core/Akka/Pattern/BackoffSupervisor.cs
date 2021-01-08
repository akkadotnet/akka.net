//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Util;

namespace Akka.Pattern
{
    /// <summary>
    /// Actor used to supervise actors with ability to restart them after back-off timeout occurred. 
    /// It's designed for cases when i.e. persistent actor stops due to journal unavailability or failure. 
    /// In this case it is better to wait before restart.
    /// </summary>
    public sealed class BackoffSupervisor : BackoffSupervisorBase
    {
        #region Messages

        /// <summary>
        /// Send this message to the <see cref="BackoffSupervisor"/> and it will reply with <see cref="CurrentChild"/> containing the `ActorRef` of the current child, if any.
        /// </summary>
        [Serializable]
        public sealed class GetCurrentChild
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetCurrentChild Instance = new GetCurrentChild();
            private GetCurrentChild() { }
        }

        /// <summary>
        /// Send this message to the <see cref="BackoffSupervisor"/> and it will reply with <see cref="CurrentChild"/> containing the `ActorRef` of the current child, if any.
        /// </summary>
        [Serializable]
        public sealed class CurrentChild
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ref">TBD</param>
            public CurrentChild(IActorRef @ref)
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; }
        }

        /// <summary>
        /// Send this message to the <see cref="BackoffSupervisor"/> and it will reset the back-off. This should be used in conjunction with `withManualReset` in <see cref="BackoffOptionsImpl"/>.
        /// </summary>
        [Serializable]
        public sealed class Reset
        {
            public static readonly Reset Instance = new Reset();
            private Reset() { }
        }

        [Serializable]
        public sealed class GetRestartCount
        {
            public static readonly GetRestartCount Instance = new GetRestartCount();
            private GetRestartCount() { }
        }

        [Serializable]
        public sealed class RestartCount
        {
            public RestartCount(int count)
            {
                Count = count;
            }

            public int Count { get; }
        }

        [Serializable]
        public sealed class StartChild : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StartChild Instance = new StartChild();
            private StartChild() { }
        }

        [Serializable]
        public sealed class ResetRestartCount : IDeadLetterSuppression
        {
            public ResetRestartCount(int current)
            {
                Current = current;
            }

            public int Current { get; }
        }

        #endregion

        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;
        private readonly SupervisorStrategy _strategy;

        public BackoffSupervisor(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor) : this(childProps, childName, minBackoff, maxBackoff, new AutoReset(minBackoff), randomFactor, Actor.SupervisorStrategy.DefaultStrategy)
        {
        }

        public BackoffSupervisor(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            IBackoffReset reset,
            double randomFactor,
            SupervisorStrategy strategy,
            object replyWhileStopped = null,
            Func<object, bool> finalStopMessage = null) : base(childProps, childName, reset, replyWhileStopped, finalStopMessage)
        {
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
            _strategy = strategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _strategy;
        }

        protected override bool Receive(object message)
        {
            return OnTerminated(message) || HandleBackoff(message);
        }

        private bool OnTerminated(object message)
        {
            if (message is Terminated terminated && terminated.ActorRef.Equals(Child))
            {
                Child = null;
                if (FinalStopMessageReceived)
                {
                    Context.Stop(Self);
                }
                else
                {
                    var maxNrOfRetries = _strategy is OneForOneStrategy oneForOne ? oneForOne.MaxNumberOfRetries : -1;
                    var nextRestartCount = RestartCountN + 1;
                    if (maxNrOfRetries == -1 || nextRestartCount <= maxNrOfRetries)
                    {
                        var restartDelay = CalculateDelay(RestartCountN, _minBackoff, _maxBackoff, _randomFactor);
                        Context.System.Scheduler.ScheduleTellOnce(restartDelay, Self, StartChild.Instance, Self);
                        RestartCountN = nextRestartCount;
                    }
                    else
                    {
                        Log.Debug($"Terminating on restart #{nextRestartCount} which exceeds max allowed restarts ({maxNrOfRetries})");
                        Context.Stop(Self);
                    }
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Props for creating a <see cref="BackoffSupervisor"/> actor.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        public static Props Props(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor)
        {
            return PropsWithSupervisorStrategy(childProps, childName, minBackoff, maxBackoff, randomFactor, Actor.SupervisorStrategy.DefaultStrategy);
        }

        /// <summary>
        /// Props for creating a <see cref="BackoffSupervisor"/> actor.
        /// 
        /// Exceptions in the child are handled with the default supervision strategy, i.e.
        /// most exceptions will immediately restart the child. You can define another
        /// supervision strategy by using [[#propsWithSupervisorStrategy]].
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxNrOfRetries">Maximum number of attempts to restart the child actor. The supervisor will terminate itself after the maxNoOfRetries is reached. In order to restart infinitely pass in `-1`.</param>
        /// <returns></returns>
        public static Props Props(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor,
            int maxNrOfRetries)
        {
            var supervisionStrategy = Actor.SupervisorStrategy.DefaultStrategy is OneForOneStrategy oneForOne
                ? oneForOne.WithMaxNrOfRetries(maxNrOfRetries)
                : Actor.SupervisorStrategy.DefaultStrategy;

            return PropsWithSupervisorStrategy(childProps, childName, minBackoff, maxBackoff, randomFactor, supervisionStrategy);
        }

        /// <summary>
        /// Props for creating a <see cref="BackoffSupervisor"/> actor from <see cref="BackoffOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="BackoffOptions"/> that specify how to construct a backoff-supervisor.</param>
        /// <returns></returns>
        public static Props Props(BackoffOptions options)
        {
            return options.Props;
        }

        /// <summary>
        /// Props for creating a <see cref="BackoffSupervisor"/> actor with a custom supervision strategy.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="strategy">The supervision strategy to use for handling exceptions in the child</param>
        public static Props PropsWithSupervisorStrategy(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor,
            SupervisorStrategy strategy)
        {
            return Actor.Props.Create(
                () => new BackoffSupervisor(childProps, childName, minBackoff, maxBackoff, new AutoReset(minBackoff), randomFactor, strategy, null, null));
        }

        /// <summary>
        /// Calculates an exponential back off delay.
        /// </summary>
        internal static TimeSpan CalculateDelay(
            int restartCount,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor)
        {
            var rand = 1.0 + ThreadLocalRandom.Current.NextDouble() * randomFactor;
            var calculateDuration = Math.Min(maxBackoff.Ticks, minBackoff.Ticks * Math.Pow(2, restartCount)) * rand;
            return calculateDuration < 0d || calculateDuration >= long.MaxValue ? maxBackoff : new TimeSpan((long)calculateDuration);
        }
    }
}
