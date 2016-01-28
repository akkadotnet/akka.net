using System;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Various stages for controlling timeouts on IO related streams (although not necessarily).
    /// 
    /// The common theme among the processing stages here that
    ///  - they wait for certain event or events to happen
    ///  - they have a timer that may fire before these events
    ///  - if the timer fires before the event happens, these stages all fail the stream
    ///  - otherwise, these streams do not interfere with the element flow, ordinary completion or failure
    /// </summary>
    internal static class Timers
    {
        public static TimeSpan IdleTimeoutCheckInterval(TimeSpan timeout)
        {
            return new TimeSpan(Math.Min(Math.Max(timeout.Ticks / 8, 100 * TimeSpan.TicksPerMillisecond), timeout.Ticks / 2));
        }
    }

    internal sealed class Initial<T> : SimpleLinearGraphStage<T>
    {
        #region InitialStageLogic
        private sealed class InitialStageLogic : TimerGraphStageLogic
        {
            private readonly Initial<T> _stage;

            public bool InitialHasPassed { get; private set; }

            public InitialStageLogic(Shape shape, Initial<T> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, onPush: () =>
                {
                    InitialHasPassed = true;
                    Push(stage.Outlet, Grab(stage.Inlet));
                });
                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (!InitialHasPassed)
                    FailStage(new TimeoutException("The first element has not yet passed through " + _stage.Timeout));
            }

            public override void PreStart()
            {
                ScheduleOnce("InitialTimeoutTimer", _stage.Timeout);
            }
        }
        #endregion

        public readonly TimeSpan Timeout;

        public Initial(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new InitialStageLogic(Shape, this);
        }
    }

    internal sealed class Completion<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic
        private sealed class CompletionStageLogic : TimerGraphStageLogic
        {
            private readonly Completion<T> _stage;

            public CompletionStageLogic(Shape shape, Completion<T> stage) : base(shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));
                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey)
            {
                FailStage(new TimeoutException("The stream has not been completed in " + _stage.Timeout));
            }

            public override void PreStart()
            {
                ScheduleOnce("CompletionTimeoutTimer", _stage.Timeout);
            }
        }
        #endregion

        public readonly TimeSpan Timeout;
        public Completion(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new CompletionStageLogic(Shape, this);
        }
    }

    internal sealed class Idle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic
        private sealed class IdleStageLogic : TimerGraphStageLogic
        {
            private readonly Idle<T> _stage;
            private DateTime _nextDeadline;

            public IdleStageLogic(Shape shape, Idle<T> stage) : base(shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow + stage.Timeout;
                SetHandler(stage.Inlet, onPush: () =>
                {
                    _nextDeadline = DateTime.UtcNow + stage.Timeout;
                    Push(stage.Outlet, Grab(stage.Inlet));
                });
                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_nextDeadline <= DateTime.UtcNow)
                    FailStage(new TimeoutException("No elements passed in the last " + _stage.Timeout));
            }

            public override void PreStart()
            {
                ScheduleRepeatedly("IdleTimeoutCheckTimer", Timers.IdleTimeoutCheckInterval(_stage.Timeout));
            }
        }
        #endregion

        public readonly TimeSpan Timeout;

        public Idle(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new IdleStageLogic(Shape, this);
        }
    }

    internal sealed class IdleTimeoutBidi<TIn, TOut> : GraphStage<BidiShape<TIn, TIn, TOut, TOut>>
    {
        #region stage logic
        private sealed class IdleTimeoutBidiStageLogic : TimerGraphStageLogic
        {
            private readonly IdleTimeoutBidi<TIn, TOut> _stage;
            private DateTime _nextDeadline;

            public IdleTimeoutBidiStageLogic(Shape shape, IdleTimeoutBidi<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow + _stage.Timeout;

                SetHandler(_stage.In1, onPush: () =>
                {
                    OnActivity();
                    Push(_stage.Out1, Grab(_stage.In1));
                },
                onUpstreamFinish: () => Complete(_stage.Out1));

                SetHandler(_stage.In2, onPush: () =>
                {
                    OnActivity();
                    Push(_stage.Out2, Grab(_stage.In2));
                },
                onUpstreamFinish: () => Complete(_stage.Out2));

                SetHandler(_stage.Out1,
                    onPull: () => Pull(_stage.In1),
                    onDownstreamFinish: () => Cancel(_stage.In1));

                SetHandler(_stage.Out2,
                    onPull: () => Pull(_stage.In2),
                    onDownstreamFinish: () => Cancel(_stage.In2));
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_nextDeadline <= DateTime.UtcNow)
                    FailStage(new TimeoutException("No elements passed in the last " + _stage.Timeout));
            }

            public override void PreStart()
            {
                ScheduleRepeatedly("IdleTimeoutBidiCheckTimer", Timers.IdleTimeoutCheckInterval(_stage.Timeout));
            }

            private void OnActivity()
            {
                _nextDeadline = DateTime.UtcNow + _stage.Timeout;
            }
        }
        #endregion

        public readonly TimeSpan Timeout;

        public readonly Inlet<TIn> In1 = new Inlet<TIn>("in1");
        public readonly Inlet<TOut> In2 = new Inlet<TOut>("in2");
        public readonly Outlet<TIn> Out1 = new Outlet<TIn>("out1");
        public readonly Outlet<TOut> Out2 = new Outlet<TOut>("out2");

        public IdleTimeoutBidi(TimeSpan timeout)
        {
            Timeout = timeout;
            InitialAttributes = Attributes.CreateName("IdleTimeoutBidi");
            Shape = new BidiShape<TIn, TIn, TOut, TOut>(In1, Out1, In2, Out2);
        }

        protected override Attributes InitialAttributes { get; }
        public override BidiShape<TIn, TIn, TOut, TOut> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new IdleTimeoutBidiStageLogic(Shape, this);
        }
    }

    internal sealed class DelayInitial<T> : GraphStage<FlowShape<T, T>>
    {
        #region stage logic
        private sealed class DelayInitialStageLogic : TimerGraphStageLogic
        {
            public const string DelayTimer = "DelayTimer";
            private readonly DelayInitial<T> _stage;
            private bool _isOpen = false;

            public DelayInitialStageLogic(Shape shape, DelayInitial<T> stage) : base(shape)
            {
                _stage = stage;
                SetHandler(_stage.In, onPush: () => Push(_stage.Out, Grab(_stage.In)));
                SetHandler(_stage.Out, onPull: () =>
                {
                    if (_isOpen) Pull(_stage.In);
                });
            }

            protected internal override void OnTimer(object timerKey)
            {
                _isOpen = true;
                if (IsAvailable(_stage.Out)) Pull(_stage.In);
            }

            public override void PreStart()
            {
                if (_stage.Delay == TimeSpan.Zero) _isOpen = true;
                else ScheduleOnce(DelayTimer, _stage.Delay);
            }
        }
        #endregion

        public readonly TimeSpan Delay;
        public readonly Inlet<T> In = new Inlet<T>("DelayInitial.in");
        public readonly Outlet<T> Out = new Outlet<T>("DelayInitial.out");

        public DelayInitial(TimeSpan delay)
        {
            Delay = delay;
            Shape = new FlowShape<T, T>(In, Out);
            InitialAttributes = Attributes.CreateName("DelayInitial");
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new DelayInitialStageLogic(Shape, this);
        }
    }

    internal sealed class IdleInject<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic
        private sealed class IdleInjectStageLogic : TimerGraphStageLogic
        {
            public const string IdleTimer = "IdleInjectTimer";
            private readonly IdleInject<TIn, TOut> _stage;
            private DateTime _nextDeadline;

            public IdleInjectStageLogic(Shape shape, IdleInject<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow + _stage.Timeout;

                SetHandler(_stage.In, onPush: () =>
                {
                    _nextDeadline = DateTime.UtcNow + _stage.Timeout;
                    CancelTimer(IdleTimer);
                    if (IsAvailable(_stage.Out))
                    {
                        Push(_stage.Out, Grab(_stage.In));
                        Pull(_stage.In);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if(!IsAvailable(_stage.In)) CompleteStage<TIn>();
                });

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (IsAvailable(_stage.In))
                    {
                        Push(_stage.Out, Grab(_stage.In));
                        if (IsClosed(_stage.In)) CompleteStage<TIn>();
                        else Pull(_stage.In);
                    }
                    else
                    {
                        if (_nextDeadline <= DateTime.UtcNow)
                        {
                            _nextDeadline = DateTime.UtcNow + _stage.Timeout;
                            Push(_stage.Out, _stage.Inject());
                        }
                        else ScheduleOnce(IdleTimer, DateTime.UtcNow - _nextDeadline);
                    }
                });
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_nextDeadline <= DateTime.UtcNow && IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, _stage.Inject());
                    _nextDeadline = DateTime.UtcNow + _stage.Timeout;
                }
            }

            public override void PreStart()
            {
                // Prefetching to ensure priority of actual upstream elements
                Pull(_stage.In);
            }
        }
        #endregion

        public readonly TimeSpan Timeout;
        public readonly Func<TOut> Inject;
        public readonly Inlet<TIn> In = new Inlet<TIn>("IdleInject.in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("IdleInject.out");

        public IdleInject(TimeSpan timeout, Func<TOut> inject)
        {
            Timeout = timeout;
            Inject = inject;

            InitialAttributes = Attributes.CreateName("IdleInject");
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new IdleInjectStageLogic(Shape, this);
        }
    }
}