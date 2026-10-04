//-----------------------------------------------------------------------
// <copyright file="Timers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Chooses the repeated check interval used by idle-timeout stages.
        /// </summary>
        /// <param name="timeout">The inactivity duration to enforce.</param>
        /// <returns>An interval between one eighth and one half of the timeout, with a 100 ms lower bound when it fits.</returns>
        public static TimeSpan IdleTimeoutCheckInterval(TimeSpan timeout)
            => new(Math.Min(Math.Max(timeout.Ticks/8, 100*TimeSpan.TicksPerMillisecond), timeout.Ticks/2));

        /// <summary>
        /// Shared timer key used by the timeout and delay stages in this class.
        /// </summary>
        public const string GraphStageLogicTimer = "GraphStageLogicTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements passing through the stage.</typeparam>
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

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected internal override void OnTimer(object timerKey)
            {
                if (!_initialHasPassed)
                    FailStage(new TimeoutException($"The first element has not yet passed through in {_stage.Timeout}."));
            }

            public override void PreStart() => ScheduleOnce(Timers.GraphStageLogicTimer, _stage.Timeout);
        }

        #endregion

        /// <summary>
        /// Gets the interval allowed for the first element to pass through.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates a stage that fails if no first element passes through before the timeout.
        /// </summary>
        /// <param name="timeout">The deadline for the first element to pass through.</param>
        public Initial(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// Supplies the default attributes for the initial-element timeout stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Initial;

        /// <summary>
        /// Creates logic that forwards elements and fails if the first does not pass in time.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the initial-element timeout.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this timeout stage.</returns>
        public override string ToString() => "InitialTimeoutTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements passing through the stage.</typeparam>
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

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected internal override void OnTimer(object timerKey)
                => FailStage(new TimeoutException($"The stream has not been completed in {_stage.Timeout}."));

            public override void PreStart() => ScheduleOnce(Timers.GraphStageLogicTimer, _stage.Timeout);
        }

        #endregion

        /// <summary>
        /// Gets the maximum time allowed for the stream to complete.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates a stage that fails if upstream does not complete before the timeout.
        /// </summary>
        /// <param name="timeout">The maximum duration before the completion timeout fails the stream.</param>
        public Completion(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// Supplies the default attributes for the completion-timeout stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Completion;

        /// <summary>
        /// Creates logic that forwards stream signals and fails if completion is not observed in time.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the completion timeout.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this timeout stage.</returns>
        public override string ToString() => "CompletionTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements passing through the stage.</typeparam>
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

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

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
        /// Gets the maximum interval allowed between upstream elements.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates a stage that fails if no upstream element arrives within the timeout interval.
        /// </summary>
        /// <param name="timeout">The maximum allowed period without an upstream element.</param>
        public Idle(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// Supplies the default attributes for the idle-timeout stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Idle;

        /// <summary>
        /// Creates logic that forwards elements and fails after an idle interval.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the idle timeout.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this timeout stage.</returns>
        public override string ToString() => "IdleTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements passing through the stage.</typeparam>
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

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

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
        /// Gets the maximum time allowed for downstream demand after an element is pushed.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates a stage that fails when downstream does not request another element in time.
        /// </summary>
        /// <param name="timeout">The maximum interval the stage waits for renewed downstream demand.</param>
        public BackpressureTimeout(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// Supplies the default attributes for the backpressure-timeout stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.BackpressureTimeout;

        /// <summary>
        /// Creates logic that tracks downstream demand and fails after a prolonged demand gap.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the backpressure timeout.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this timeout stage.</returns>
        public override string ToString() => "BackpressureTimeout";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">The type of elements on the first flow direction.</typeparam>
    /// <typeparam name="TOut">The type of elements on the second flow direction.</typeparam>
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
                    onDownstreamFinish: cause => Cancel(_stage.In2, cause));
            }

            public void OnPush()
            {
                OnActivity();
                Push(_stage.Out1, Grab(_stage.In1));
            }

            public void OnUpstreamFinish() => Complete(_stage.Out1);

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.In1);

            public void OnDownstreamFinish(Exception cause) => Cancel(_stage.In1, cause);

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
        /// Gets the maximum inactivity interval across both flow directions.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Gets the inlet for elements in the first direction.
        /// </summary>
        public readonly Inlet<TIn> In1 = new("in1");
        /// <summary>
        /// Gets the inlet for elements in the second direction.
        /// </summary>
        public readonly Inlet<TOut> In2 = new("in2");
        /// <summary>
        /// Gets the outlet for elements in the first direction.
        /// </summary>
        public readonly Outlet<TIn> Out1 = new("out1");
        /// <summary>
        /// Gets the outlet for elements in the second direction.
        /// </summary>
        public readonly Outlet<TOut> Out2 = new("out2");

        /// <summary>
        /// Creates a bidirectional flow stage that fails if neither direction has activity before the timeout.
        /// </summary>
        /// <param name="timeout">The maximum period without an element passing through either direction.</param>
        public IdleTimeoutBidi(TimeSpan timeout)
        {
            Timeout = timeout;
            Shape = new BidiShape<TIn, TIn, TOut, TOut>(In1, Out1, In2, Out2);
        }

        /// <summary>
        /// Supplies the default attributes for the bidirectional idle-timeout stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IdleTimeoutBidi;

        /// <summary>
        /// Gets the bidirectional shape containing both element paths.
        /// </summary>
        public override BidiShape<TIn, TIn, TOut, TOut> Shape { get; }

        /// <summary>
        /// Creates logic that forwards both directions and resets the idle deadline on activity.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the bidirectional idle timeout.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this timeout stage.</returns>
        public override string ToString() => "IdleTimeoutBidi";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements passing through the stage.</typeparam>
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

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

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
        /// Gets the delay before the stage begins pulling upstream.
        /// </summary>
        public readonly TimeSpan Delay;


        /// <summary>
        /// Creates a stage that holds off pulling upstream until the initial delay has elapsed.
        /// </summary>
        /// <param name="delay">The delay before upstream pulling is enabled.</param>
        public DelayInitial(TimeSpan delay) : base("DelayInitial")
        {
            Delay = delay;
        }

        /// <summary>
        /// Supplies the default attributes for the initial-delay stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.DelayInitial;

        /// <summary>
        /// Creates logic that enables upstream pulls after the configured delay.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for the initial delay.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this delay stage.</returns>
        public override string ToString() => "DelayTimer";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">The input element type; it must also be assignable to <typeparamref name="TOut"/>.</typeparam>
    /// <typeparam name="TOut">The output element type emitted for input or injected values.</typeparam>
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
                _nextDeadline = 0; // Sentinel: deadline not set until first pull

                SetHandler(_stage._in, this);
                SetHandler(_stage._out, this);
            }

            private void ResetDeadline()
            {
                _nextDeadline = DateTime.UtcNow.Ticks + _stage._timeout.Ticks;
            }

            public void OnPush()
            {
                ResetDeadline();
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
                    ResetDeadline();
                    if (IsClosed(_stage._in))
                        CompleteStage();
                    else
                        Pull(_stage._in);
                }
                else
                {
                    var time = DateTime.UtcNow.Ticks;

                    // Initialize deadline on first pull if not set yet
                    if (_nextDeadline == 0)
                        ResetDeadline();

                    if (_nextDeadline - time < 0)
                    {
                        Push(_stage._out, _stage._inject());
                        ResetDeadline();
                    }
                    else
                        ScheduleOnce(Timers.GraphStageLogicTimer, TimeSpan.FromTicks(_nextDeadline - time));
                }
            }

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            protected internal override void OnTimer(object timerKey)
            {
                // Don't inject if upstream element is already available
                if (IsAvailable(_stage._in))
                    return;

                var time = DateTime.UtcNow.Ticks;
                if ((_nextDeadline - time < 0) && IsAvailable(_stage._out))
                {
                    Push(_stage._out, _stage._inject());
                    ResetDeadline();
                }
            }

            // Prefetching to ensure priority of actual upstream elements
            public override void PreStart() => Pull(_stage._in);
        }

        #endregion

        private readonly TimeSpan _timeout;
        private readonly Func<TOut> _inject;
        private readonly Inlet<TIn> _in = new("IdleInject.in");
        private readonly Outlet<TOut> _out = new("IdleInject.out");

        /// <summary>
        /// Creates a flow that emits a generated value after the timeout when no upstream element is available.
        /// </summary>
        /// <param name="timeout">The idle interval after which an output is generated.</param>
        /// <param name="inject">Creates the output value to emit when the interval expires.</param>
        public IdleInject(TimeSpan timeout, Func<TOut> inject)
        {
            _timeout = timeout;
            _inject = inject;
            
            Shape = new FlowShape<TIn, TOut>(_in, _out);
        }

        /// <summary>
        /// Supplies the default attributes for the idle-injection stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IdleInject;

        /// <summary>
        /// Gets the flow shape for the input and output element types.
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// Creates logic that prefers available upstream elements and injects a value after idle time.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to this stage.</param>
        /// <returns>The timer logic for idle-value injection.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the diagnostic stage name.
        /// </summary>
        /// <returns>The fixed name of this injection stage.</returns>
        public override string ToString() => "IdleTimer";
    }
}
