//-----------------------------------------------------------------------
// <copyright file="Throttle.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Akka.Event;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public class Throttle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private const string TimerName = "ThrottleTimer";
            private readonly Throttle<T> _stage;
            private readonly TickTimeTokenBucket _tokenBucket;
            private readonly bool _enforcing;
            private readonly Decider _decider;

            private bool _willStop;
            private Option<T> _currentElement;

            public Logic(Throttle<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _tokenBucket = new TickTimeTokenBucket(stage._maximumBurst, stage._ticksBetweenTokens);
                _enforcing = stage._mode == ThrottleMode.Enforcing;

                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
                
                SetHandler(_stage.Inlet, this);
                SetHandler(_stage.Outlet, this);
            }

            public void OnPush()
            {
                var element = Grab(_stage.Inlet);
                int cost;
                try
                {
                    cost = _stage._costCalculation(element);
                }
                catch (Exception e)
                {
                    var strategy = _decider(e);
                    Log.Error(e, "An exception occured inside Throttle while calculating cost for element [{0}]. Supervision strategy: {1}", element, strategy);
                    switch (strategy)
                    {
                        case Directive.Stop:
                            FailStage(e);
                            return;
                        
                        case Directive.Resume:
                        case Directive.Restart:
                            if (!HasBeenPulled(_stage.Inlet))
                                TryPull(_stage.Inlet);
                            return;
                        
                        default:
                            throw new AggregateException($"Unknown SupervisionStrategy directive: {strategy}", e);
                    }
                }
                
                var delayTicks = _tokenBucket.Offer(cost);

                if (delayTicks == 0)
                    Push(_stage.Outlet, element);
                else
                {
                    if (_enforcing)
                        throw new OverflowException("Maximum throttle throughput exceeded.");

                    _currentElement = element;
                    ScheduleOnce(TimerName, TimeSpan.FromTicks(delayTicks));
                }
            }

            public void OnUpstreamFinish()
            {
                if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName))
                    _willStop = true;
                else
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected internal override void OnTimer(object timerKey)
            {
                Push(_stage.Outlet, _currentElement.Value);
                _currentElement = Option<T>.None;

                if (_willStop)
                    CompleteStage();
            }

            public override void PreStart() => _tokenBucket.Init();
        }

        #endregion
        
        private readonly int _maximumBurst;
        private readonly Func<T, int> _costCalculation;
        private readonly ThrottleMode _mode;
        private readonly long _ticksBetweenTokens;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cost">TBD</param>
        /// <param name="per">TBD</param>
        /// <param name="maximumBurst">TBD</param>
        /// <param name="costCalculation">TBD</param>
        /// <param name="mode">TBD</param>
        public Throttle(int cost, TimeSpan per, int maximumBurst, Func<T, int> costCalculation, ThrottleMode mode)
        {
            _maximumBurst = maximumBurst;
            _costCalculation = costCalculation;
            _mode = mode;

            // There is some loss of precision here because of rounding, but this only happens if nanosBetweenTokens is very
            // small which is usually at rates where that precision is highly unlikely anyway as the overhead of this stage
            // is likely higher than the required accuracy interval.
            _ticksBetweenTokens = per.Ticks/cost;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Throttle";
    }
}
