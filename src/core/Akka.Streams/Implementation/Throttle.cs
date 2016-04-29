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

namespace Akka.Streams.Implementation
{
    internal class Throttle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerName = "ThrottleTimer";
            private readonly Throttle<T> _stage;
            private readonly long _speed;
            private readonly long _scaledMaximumBurst;

            private long _previousTime;
            private bool _willStop;
            private long _lastTokens;
            private Option<T> _currentElement;

            public Logic(Throttle<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _lastTokens = stage._maximumBurst;
                _previousTime = Now;
                _speed = (long)(((double)stage._cost/stage._per.Ticks)*1073741824);
                _scaledMaximumBurst = Scale(_stage._maximumBurst);

                SetHandler(_stage.Inlet, onPush: () =>
                {
                    var element = Grab(_stage.Inlet);
                    var elementCost = Scale(_stage._costCalculation(element));

                    if (_lastTokens >= elementCost)
                    {
                        _lastTokens -= elementCost;
                        Push(_stage.Outlet, element);
                    }
                    else
                    {
                        var currentTime = Now;
                        var currentTokens = Math.Min((currentTime - _previousTime)*_speed + _lastTokens,
                            _scaledMaximumBurst);
                        if (currentTokens < elementCost)
                        {
                            switch (_stage._mode)
                            {
                                case ThrottleMode.Shaping:
                                    _currentElement = element;
                                    var waitTime = (elementCost - currentTokens)/_speed;
                                    _previousTime = currentTime + waitTime;
                                    ScheduleOnce(TimerName, TimeSpan.FromTicks(waitTime));
                                    break;
                                case ThrottleMode.Enforcing:
                                    FailStage(new OverflowException("Maximum throttle throughput exceeded"));
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                        else
                        {
                            _lastTokens = currentTokens - elementCost;
                            _previousTime = currentTime;
                            Push(_stage.Outlet, element);
                        }
                    }
                }, onUpstreamFinish: () =>
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
                _lastTokens = 0;
                if (_willStop)
                    CompleteStage();
            }

            public override void PreStart() => _previousTime = Now;

            private long Scale(int n) => ((long) n) << 30;

            private long Now => DateTime.Now.Ticks;
        }

        #endregion

        private readonly int _cost;
        private readonly TimeSpan _per;
        private readonly int _maximumBurst;
        private readonly Func<T, int> _costCalculation;
        private readonly ThrottleMode _mode;

        public Throttle(int cost, TimeSpan per, int maximumBurst, Func<T, int> costCalculation, ThrottleMode mode)
        {
            _cost = cost;
            _per = per;
            _maximumBurst = maximumBurst;
            _costCalculation = costCalculation;
            _mode = mode;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}