using System;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    internal class Throttle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic
        private sealed class ThrottleStageLogic : TimerGraphStageLogic
        {
            public const string TimerName = "ThrottleTimer";
            private readonly Throttle<T> _stage;
            private readonly long _speed;
            private readonly long _scaledMaximumBurst;

            private DateTime _previousTime;
            private bool _willStop = false;
            private long _lastTokens = 0L;
            private object _currentElement = null;

            public ThrottleStageLogic(Shape shape, Throttle<T> stage) : base(shape)
            {
                _stage = stage;
                _speed = (long)((_stage.Cost / _stage.Per.TotalMilliseconds) * 1024 * 1024);
                _scaledMaximumBurst = Scale(_stage.MaximumBurst);

                SetHandler(_stage.Inlet, onPush: () =>
                {
                    var timeElapsed = DateTime.UtcNow - _previousTime;
                    var currentTokens = Math.Min(((long)timeElapsed.TotalMilliseconds) * _speed + _lastTokens, _scaledMaximumBurst);
                    var element = Grab(_stage.Inlet);
                    var elementCost = Scale(_stage.CostCalculation(element));
                    if (currentTokens < elementCost)
                    {
                        switch (_stage.Mode)
                        {
                            case ThrottleMode.Shaping:
                                _currentElement = element;
                                ScheduleOnce(TimerName, TimeSpan.FromMilliseconds((elementCost - currentTokens) / _speed));
                                break;
                            case ThrottleMode.Enforcing:
                                FailStage(new OverflowException("Maximum throttle throughtput exceeded"));
                                break;
                        }
                    }
                    else
                    {
                        _lastTokens = currentTokens - elementCost;
                        _previousTime = DateTime.UtcNow;
                        Push(_stage.Outlet, element);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName)) _willStop = true;
                    else CompleteStage<T>();
                });

                SetHandler(_stage.Outlet, onPull: () => Pull(_stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey)
            {
                Push(_stage.Outlet, (T)_currentElement);
                _currentElement = null;
                _previousTime = DateTime.UtcNow;
                _lastTokens = 0;
                if (_willStop) CompleteStage<T>();
            }

            public override void PreStart()
            {
                _previousTime = DateTime.UtcNow;
            }

            private long Scale(int n)
            {
                return ((long)n) << 20;
            }
        }
        #endregion

        public readonly int Cost;
        public readonly TimeSpan Per;
        public readonly int MaximumBurst;
        public readonly Func<T, int> CostCalculation;
        public readonly ThrottleMode Mode;

        public Throttle(int cost, TimeSpan per, int maximumBurst, Func<T, int> costCalculation, ThrottleMode mode)
        {
            Cost = cost;
            Per = per;
            MaximumBurst = maximumBurst;
            CostCalculation = costCalculation;
            Mode = mode;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ThrottleStageLogic(Shape, this);
        }
    }
}