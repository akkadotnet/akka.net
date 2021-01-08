//-----------------------------------------------------------------------
// <copyright file="BackoffOptions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Pattern
{
    /// <summary>
    /// Builds back-off options for creating a back-off supervisor. You can pass <see cref="BackoffOptions"/> to <see cref="BackoffSupervisor.Props"/>.
    /// </summary>
    public static class Backoff
    {
        /// <summary>
        /// Back-off options for creating a back-off supervisor actor that expects a child actor to restart on failure.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded one which accepts maxNrOfRetries instead.")]
        public static BackoffOptions OnFailure(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            return OnFailure(childProps, childName, minBackoff, maxBackoff, randomFactor, -1);
        }

        /// <summary>
        /// Back-off options for creating a back-off supervisor actor that expects a child actor to restart on failure.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxNrOfRetries">Maximum number of attempts to restart the child actor. The supervisor will terminate itself after the maxNoOfRetries is reached. In order to restart infinitely pass in `-1`</param>
        public static BackoffOptions OnFailure(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxNrOfRetries)
        {
            return new BackoffOptionsImpl(RestartImpliesFailure.Instance, childProps, childName, minBackoff, maxBackoff, randomFactor)
                .WithMaxNrOfRetries(maxNrOfRetries);
        }

        /// <summary>
        /// Back-off options for creating a back-off supervisor actor that expects a child actor to stop on failure.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded one which accepts maxNrOfRetries instead.")]
        public static BackoffOptions OnStop(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            return OnStop(childProps, childName, minBackoff, maxBackoff, randomFactor, -1);
        }

        /// <summary>
        /// Back-off options for creating a back-off supervisor actor that expects a child actor to stop on failure.
        /// </summary>
        /// <param name="childProps">The <see cref="Akka.Actor.Props"/> of the child actor that will be started and supervised</param>
        /// <param name="childName">Name of the child actor</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxNrOfRetries">Maximum number of attempts to restart the child actor. The supervisor will terminate itself after the maxNoOfRetries is reached. In order to restart infinitely pass in `-1`</param>
        public static BackoffOptions OnStop(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxNrOfRetries)
        {
            return new BackoffOptionsImpl(StopImpliesFailure.Instance, childProps, childName, minBackoff, maxBackoff, randomFactor)
                .WithMaxNrOfRetries(maxNrOfRetries);
        }
    }

    public abstract class BackoffOptions
    {
        /// <summary>
        /// Returns a new <see cref="BackoffOptions"/> with automatic back-off reset. The back-off algorithm is reset if the child does not crash within the specified `resetBackoff`.
        /// </summary>
        /// <param name="resetBackoff">The back-off is reset if the child does not crash within this duration</param>
        public abstract BackoffOptions WithAutoReset(TimeSpan resetBackoff);

        /// <summary>
        /// Returns a new <see cref="BackoffOptions"/> with manual back-off reset. The back-off is only reset if the child sends a `BackoffSupervisor.Reset` to its parent(the backoff-supervisor actor).
        /// </summary>
        public abstract BackoffOptions WithManualReset();

        /// <summary>
        /// Returns a new <see cref="BackoffOptions"/> with the supervisorStrategy.
        /// </summary>
        /// <param name="supervisorStrategy">The <see cref="SupervisorStrategy"/> that the back-off supervisor will use. The default supervisor strategy is used as fallback if the specified SupervisorStrategy (its decider) does not explicitly handle an exception</param>
        public abstract BackoffOptions WithSupervisorStrategy(OneForOneStrategy supervisorStrategy);

        /// <summary>
        /// Returns a new <see cref="BackoffOptions"/> with a default <see cref="SupervisorStrategy.StoppingStrategy"/>. The default supervisor strategy is used as fallback for throwables not handled by <see cref="SupervisorStrategy.StoppingStrategy"/>.
        /// </summary>
        public abstract BackoffOptions WithDefaultStoppingStrategy();

        /// <summary>
        /// Returns a new BackoffOptions with a constant reply to messages that the supervisor receives while its child is stopped. By default, a message received while the child is stopped is forwarded to `deadLetters`. With this option, the supervisor will reply to the sender instead.
        /// </summary>
        /// <param name="replyWhileStopped">The message that the supervisor will send in response to all messages while its child is stopped.</param>
        public abstract BackoffOptions WithReplyWhileStopped(object replyWhileStopped);

        /// <summary>
        /// Returns a new BackoffOptions with a maximum number of retries to restart the child actor. By default, the supervisor will retry infinitely. With this option, the supervisor will terminate itself after the maxNoOfRetries is reached.
        /// </summary>
        /// <param name="maxNrOfRetries">The number of times a child actor is allowed to be restarted, negative value means no limit, if the limit is exceeded the child actor is stopped</param>
        /// <returns></returns>
        public abstract BackoffOptions WithMaxNrOfRetries(int maxNrOfRetries);

        /// <summary>
        /// Predicate evaluated for each message, if it returns true and the supervised actor is stopped then the supervisor will stop its self. If it returns true while the supervised actor is running then it will be forwarded to the supervised actor and when the supervised actor stops itself the supervisor will stop itself.
        /// </summary>
        public abstract BackoffOptions WithFinalStopMessage(Func<object, bool> isFinalStopMessage);

        /// <summary>
        /// Returns the props to create the back-off supervisor.
        /// </summary>
        internal abstract Props Props { get; }
    }

    internal sealed class BackoffOptionsImpl : BackoffOptions
    {
        private readonly IBackoffType _backoffType;
        private readonly Props _childProps;
        private readonly string _childName;
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;
        private readonly IBackoffReset _reset;
        private readonly OneForOneStrategy _strategy;
        private readonly object _replyWhileStopped;
        private readonly Func<object, bool> _finalStopMessage;

        public BackoffOptionsImpl(IBackoffType backoffType, Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, IBackoffReset reset = null) 
            : this(backoffType, childProps, childName, minBackoff, maxBackoff, randomFactor, reset, new OneForOneStrategy(SupervisorStrategy.DefaultDecider))
        {
        }

        public BackoffOptionsImpl(IBackoffType backoffType, Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, IBackoffReset reset, OneForOneStrategy strategy, object replyWhileStopped = null, Func<object, bool> finalStopMessage = null)
        {
            _backoffType = backoffType ?? RestartImpliesFailure.Instance;
            _childProps = childProps;
            _childName = childName;
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
            _reset = reset ?? new AutoReset(_minBackoff);
            _strategy = strategy;
            _replyWhileStopped = replyWhileStopped;
            _finalStopMessage = finalStopMessage;
        }

        public override BackoffOptions WithAutoReset(TimeSpan resetBackoff)
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, new AutoReset(resetBackoff), _strategy, _replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithManualReset()
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, new ManualReset(), _strategy, _replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithSupervisorStrategy(OneForOneStrategy supervisorStrategy)
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, _reset, supervisorStrategy, _replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithDefaultStoppingStrategy()
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, _reset, new OneForOneStrategy(_strategy.MaxNumberOfRetries, null, SupervisorStrategy.StoppingStrategy.Decider), _replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithReplyWhileStopped(object replyWhileStopped)
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, _reset, _strategy, replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithMaxNrOfRetries(int maxNrOfRetries)
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, _reset, _strategy.WithMaxNrOfRetries(maxNrOfRetries), _replyWhileStopped, _finalStopMessage);
        }

        public override BackoffOptions WithFinalStopMessage(Func<object, bool> isFinalStopMessage)
        {
            return new BackoffOptionsImpl(_backoffType, _childProps, _childName, _minBackoff, _maxBackoff, _randomFactor, _reset, _strategy, _replyWhileStopped, isFinalStopMessage);
        }

        internal override Props Props
        {
            get
            {
                if (_minBackoff <= TimeSpan.Zero) throw new ArgumentException("MinBackoff must be greater than 0");
                if (_maxBackoff < _minBackoff) throw new ArgumentException("MaxBackoff must be greater than MinBackoff");
                if (_randomFactor < 0.0 || _randomFactor > 1.0) throw new ArgumentException("RandomFactor must be between 0.0 and 1.0");
                
                if (_reset is AutoReset autoReset)
                {
                    if (_minBackoff > autoReset.ResetBackoff && autoReset.ResetBackoff > _maxBackoff)
                        throw new ArgumentException();
                }
                else
                {
                    // ignore
                }

                switch (_backoffType)
                {
                    case RestartImpliesFailure _:
                        return Props.Create(() => new BackoffOnRestartSupervisor(_childProps, _childName, _minBackoff, _maxBackoff, _reset, _randomFactor, _strategy, _replyWhileStopped, _finalStopMessage));
                    case StopImpliesFailure _:
                        return Props.Create(() => new BackoffSupervisor(_childProps, _childName, _minBackoff, _maxBackoff, _reset, _randomFactor, _strategy, _replyWhileStopped, _finalStopMessage));
                    default:
                        return Props.Empty;
                }
            }
        }
    }

    internal interface IBackoffType
    {
    }

    internal sealed class StopImpliesFailure : IBackoffType
    {
        public static readonly StopImpliesFailure Instance = new StopImpliesFailure();
        private StopImpliesFailure() { }
    }

    internal sealed class RestartImpliesFailure : IBackoffType
    {
        public static readonly RestartImpliesFailure Instance = new RestartImpliesFailure();
        private RestartImpliesFailure() { }
    }

    public interface IBackoffReset
    {
    }

    internal sealed class ManualReset : IBackoffReset
    {
    }

    internal sealed class AutoReset : IBackoffReset
    {
        public AutoReset(TimeSpan resetBackoff)
        {
            ResetBackoff = resetBackoff;
        }

        public TimeSpan ResetBackoff { get; }
    }
}
