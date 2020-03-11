//-----------------------------------------------------------------------
// <copyright file="Timers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
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
    [InternalApi]
    public static class Timers
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static TimeSpan IdleTimeoutCheckInterval(TimeSpan timeout)
            => new TimeSpan(Math.Min(Math.Max(timeout.Ticks/8, 100*TimeSpan.TicksPerMillisecond), timeout.Ticks/2));

        /// <summary>
        /// TBD
        /// </summary>
        public const string GraphStageLogicTimer = "GraphStageLogicTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Initial<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Initial<T> _stage;
            private bool _initialHasPassed;

            public Logic(Initial<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                _initialHasPassed = true;
                Push(_stage.Outlet, Grab(_stage.Inlet));
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                if (!_initialHasPassed)
                    FailStage(new TimeoutException($"The first element has not yet passed through in {_stage.Timeout}."));
            }

            public override void PreStart() => ScheduleOnce(Timers.GraphStageLogicTimer, _stage.Timeout);
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public Initial(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Initial;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "InitialTimeoutTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Completion<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Completion<T> _stage;

            public Logic(Completion<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
                => FailStage(new TimeoutException($"The stream has not been completed in {_stage.Timeout}."));

            public override void PreStart() => ScheduleOnce(Timers.GraphStageLogicTimer, _stage.Timeout);
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public Completion(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Completion;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "CompletionTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Idle<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Idle<T> _stage;
            private long _nextDeadline;

            public Logic(Idle<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow.Ticks + stage.Timeout.Ticks;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                _nextDeadline = DateTime.UtcNow.Ticks + _stage.Timeout.Ticks;
                Push(_stage.Outlet, Grab(_stage.Inlet));
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                if (_nextDeadline - DateTime.UtcNow.Ticks < 0)
                    FailStage(new TimeoutException($"No elements passed in the last {_stage.Timeout}."));
            }

            public override void PreStart()
                => ScheduleRepeatedly(Timers.GraphStageLogicTimer, Timers.IdleTimeoutCheckInterval(_stage.Timeout));
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public Idle(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Idle;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "IdleTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class BackpressureTimeout<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly BackpressureTimeout<T> _stage;
            private long _nextDeadline;
            private bool _waitingDemand = true;

            public Logic(BackpressureTimeout<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow.Ticks + stage.Timeout.Ticks;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                Push(_stage.Outlet, Grab(_stage.Inlet));
                _nextDeadline = DateTime.UtcNow.Ticks + _stage.Timeout.Ticks;
                _waitingDemand = true;
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                _waitingDemand = false;
                Pull(_stage.Inlet);
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                if (_waitingDemand && (_nextDeadline - DateTime.UtcNow.Ticks < 0))
                    FailStage(new TimeoutException($"No demand signalled in the last {_stage.Timeout}."));
            }

            public override void PreStart()
                => ScheduleRepeatedly(Timers.GraphStageLogicTimer, Timers.IdleTimeoutCheckInterval(_stage.Timeout));
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public BackpressureTimeout(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.BackpressureTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "BackpressureTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class IdleTimeoutBidi<TIn, TOut> : GraphStage<BidiShape<TIn, TIn, TOut, TOut>>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly IdleTimeoutBidi<TIn, TOut> _stage;
            private long _nextDeadline;

            public Logic(IdleTimeoutBidi<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow.Ticks + _stage.Timeout.Ticks;

                SetHandler(_stage.In1, this);
                SetHandler(_stage.Out1, this);

                SetHandler(_stage.In2, onPush: () =>
                {
                    OnActivity();
                    Push(_stage.Out2, Grab(_stage.In2));
                },
                onUpstreamFinish: () => Complete(_stage.Out2));

                SetHandler(_stage.Out2,
                    onPull: () => Pull(_stage.In2),
                    onDownstreamFinish: () => Cancel(_stage.In2));
            }

            public void OnPush()
            {
                OnActivity();
                Push(_stage.Out1, Grab(_stage.In1));
            }

            public void OnUpstreamFinish() => Complete(_stage.Out1);

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.In1);

            public void OnDownstreamFinish() => Cancel(_stage.In1);

            protected internal override void OnTimer(object timerKey)
            {
                if (_nextDeadline - DateTime.UtcNow.Ticks < 0)
                    FailStage(new TimeoutException($"No elements passed in the last {_stage.Timeout}."));
            }

            public override void PreStart()
                => ScheduleRepeatedly(Timers.GraphStageLogicTimer, Timers.IdleTimeoutCheckInterval(_stage.Timeout));

            private void OnActivity() => _nextDeadline = DateTime.UtcNow.Ticks + _stage.Timeout.Ticks;
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn> In1 = new Inlet<TIn>("in1");
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TOut> In2 = new Inlet<TOut>("in2");
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TIn> Out1 = new Outlet<TIn>("out1");
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut> Out2 = new Outlet<TOut>("out2");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public IdleTimeoutBidi(TimeSpan timeout)
        {
            Timeout = timeout;
            Shape = new BidiShape<TIn, TIn, TOut, TOut>(In1, Out1, In2, Out2);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IdleTimeoutBidi;

        /// <summary>
        /// TBD
        /// </summary>
        public override BidiShape<TIn, TIn, TOut, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "IdleTimeoutBidi";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class DelayInitial<T> : SimpleLinearGraphStage<T>
    {
        #region stage logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly DelayInitial<T> _stage;
            private bool _isOpen;

            public Logic(DelayInitial<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(_stage.Inlet, this);
                SetHandler(_stage.Outlet, this);
            }

            public void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (_isOpen)
                    Pull(_stage.Inlet);
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                _isOpen = true;
                if (IsAvailable(_stage.Outlet))
                    Pull(_stage.Inlet);
            }

            public override void PreStart()
            {
                if (_stage.Delay == TimeSpan.Zero)
                    _isOpen = true;
                else
                    ScheduleOnce(Timers.GraphStageLogicTimer, _stage.Delay);
            }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Delay;


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        public DelayInitial(TimeSpan delay) : base("DelayInitial")
        {
            Delay = delay;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.DelayInitial;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "DelayTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class IdleInject<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>> where TIn : TOut
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly IdleInject<TIn, TOut> _stage;
            private long _nextDeadline;

            public Logic(IdleInject<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                _nextDeadline = DateTime.UtcNow.Ticks + _stage._timeout.Ticks;

                SetHandler(_stage._in, this);
                SetHandler(_stage._out, this);
            }

            public void OnPush()
            {
                _nextDeadline = DateTime.UtcNow.Ticks + _stage._timeout.Ticks;
                CancelTimer(Timers.GraphStageLogicTimer);
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, Grab(_stage._in));
                    Pull(_stage._in);
                }
            }

            public void OnUpstreamFinish()
            {
                if (!IsAvailable(_stage._in))
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (IsAvailable(_stage._in))
                {
                    Push(_stage._out, Grab(_stage._in));
                    if (IsClosed(_stage._in))
                        CompleteStage();
                    else
                        Pull(_stage._in);
                }
                else
                {
                    var time = DateTime.UtcNow.Ticks;
                    if (_nextDeadline - time < 0)
                    {
                        _nextDeadline = time + _stage._timeout.Ticks;
                        Push(_stage._out, _stage._inject());
                    }
                    else
                        ScheduleOnce(Timers.GraphStageLogicTimer, TimeSpan.FromTicks(_nextDeadline - time));
                }
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                var time = DateTime.UtcNow.Ticks;
                if ((_nextDeadline - time < 0) && IsAvailable(_stage._out))
                {
                    Push(_stage._out, _stage._inject());
                    _nextDeadline = DateTime.UtcNow.Ticks + _stage._timeout.Ticks;
                }
            }

            // Prefetching to ensure priority of actual upstream elements
            public override void PreStart() => Pull(_stage._in);
        }

        #endregion

        private readonly TimeSpan _timeout;
        private readonly Func<TOut> _inject;
        private readonly Inlet<TIn> _in = new Inlet<TIn>("IdleInject.in");
        private readonly Outlet<TOut> _out = new Outlet<TOut>("IdleInject.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <param name="inject">TBD</param>
        public IdleInject(TimeSpan timeout, Func<TOut> inject)
        {
            _timeout = timeout;
            _inject = inject;
            
            Shape = new FlowShape<TIn, TOut>(_in, _out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IdleInject;

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "IdleTimer";
    }
}
