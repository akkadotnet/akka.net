//-----------------------------------------------------------------------
// <copyright file="Throttle.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    internal class Throttle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerName = "ThrottleTimer";
            private readonly Throttle<T> _stage;
            private readonly TickTimeTokenBucket _tokenBucket;
            private readonly bool _enforcing;

            private bool _willStop;
            private Option<T> _currentElement;

            public Logic(Throttle<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _tokenBucket = new TickTimeTokenBucket(stage._maximumBurst, stage._ticksBetweenTokens);
                _enforcing = stage._mode == ThrottleMode.Enforcing;

                SetHandler(_stage.Inlet,
                    onPush: () =>
                    {
                        var element = Grab(stage.Inlet);
                        var cost = stage._costCalculation(element);
                        var delayTicks = _tokenBucket.Offer(cost);

                        if (delayTicks == 0)
                            Push(stage.Outlet, element);
                        else
                        {
                            if (_enforcing)
                                throw new OverflowException("Maximum throttle throughput exceeded.");

                            _currentElement = element;
                            ScheduleOnce(TimerName, TimeSpan.FromTicks(delayTicks));
                        }
                    },
                    onUpstreamFinish: () =>
                    {
                        if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName))
                            _willStop = true;
                        else
                            CompleteStage();
                    });

                SetHandler(_stage.Outlet, onPull: () => Pull(_stage.Inlet));
            }

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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Throttle";
    }
}