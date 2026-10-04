//-----------------------------------------------------------------------
// <copyright file="Timed.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Annotations;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using static Akka.Streams.Extra.Timed;

namespace Akka.Streams.Extra
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Provides operations needed to implement the <see cref="TimedFlowDsl"/> and <see cref="TimedSourceDsl"/>
    /// </summary>
    internal static class TimedOps
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// Measures elapsed time from the first source element until the measured stream receives upstream completion or failure, then reports it.
        /// </summary>
        /// <typeparam name="TIn">The element type of the source and input to the measured operations.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the measured operations.</typeparam>
        /// <typeparam name="TMat">The source's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The materialized value type returned by the measured operations.</typeparam>
        /// <param name="source">The source whose elements pass through the measured operations.</param>
        /// <param name="measuredOps">The source transformation whose elapsed processing time is measured.</param>
        /// <param name="onComplete">The callback invoked when the measured stream receives upstream completion or failure.</param>
        /// <returns>The transformed source with the materialized value returned by <paramref name="measuredOps"/>.</returns>
        [InternalApi]
        public static Source<TOut, TMat2> Timed<TIn, TOut, TMat, TMat2>(Source<TIn, TMat> source, Func<Source<TIn, TMat>, Source<TOut, TMat2>> measuredOps, Action<TimeSpan> onComplete)
        {
            var ctx = new TimedFlowContext();

            var startTimed = Flow.Create<TIn>().Via(new StartTimed<TIn>(ctx)).Named("startTimed");
            var stopTimed = Flow.Create<TOut>().Via(new StopTime<TOut>(ctx, onComplete)).Named("stopTimed");

            return measuredOps(source.Via(startTimed)).Via(stopTimed);
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Measures elapsed time from the first flow output element until the measured stream receives upstream completion or failure, then reports it.
        /// </summary>
        /// <typeparam name="TIn">The input element type of the flow.</typeparam>
        /// <typeparam name="TOut">The output element type of the flow before applying <paramref name="measuredOps"/>.</typeparam>
        /// <typeparam name="TOut2">The output element type produced by the measured operations.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The materialized value type returned by the measured operations.</typeparam>
        /// <param name="flow">The flow whose output passes through the measured operations.</param>
        /// <param name="measuredOps">The flow transformation whose elapsed processing time is measured.</param>
        /// <param name="onComplete">The callback invoked when the measured stream receives upstream completion or failure.</param>
        /// <returns>The transformed flow with the materialized value returned by <paramref name="measuredOps"/>.</returns>
        public static Flow<TIn, TOut2, TMat2> Timed<TIn, TOut, TOut2, TMat, TMat2>(Flow<TIn, TOut, TMat> flow, Func<Flow<TIn, TOut, TMat>, Flow<TIn, TOut2, TMat2>> measuredOps, Action<TimeSpan> onComplete)
        {
            // todo is there any other way to provide this for Flow, without duplicating impl?
            // they do share a super-type (FlowOps), but all operations of FlowOps return path dependant type
            var ctx = new TimedFlowContext();

            var startTimed = Flow.Create<TOut>().Via(new StartTimed<TOut>(ctx)).Named("startTimed");
            var stopTimed = Flow.Create<TOut2>().Via(new StopTime<TOut2>(ctx, onComplete)).Named("stopTimed");

            return measuredOps(flow.Via(startTimed)).Via(stopTimed);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Provides operations needed to implement the <see cref="TimedFlowDsl"/> and <see cref="TimedSourceDsl"/>
    /// </summary>
    internal static class TimedIntervalBetweenOps
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// Measures rolling interval between immediately subsequent `matching(o: O)` elements.
        /// </summary>
        /// <typeparam name="TIn">The element type of the flow.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <param name="flow">The flow whose matching elements are timed.</param>
        /// <param name="matching">Selects the elements used to measure consecutive intervals.</param>
        /// <param name="onInterval">The callback that receives each interval after the first matching element.</param>
        /// <returns>The flow with its element and materialized value types preserved.</returns>
        [InternalApi]
        public static IFlow<TIn, TMat> TimedIntervalBetween<TIn, TMat>(IFlow<TIn, TMat> flow, Func<TIn, bool> matching, Action<TimeSpan> onInterval)
        {
            var timedInterval =
                Flow.Create<TIn>()
                    .Via(new TimedInterval<TIn>(matching, onInterval))
                    .Named("timedInterval");

            return flow.Via(timedInterval);
        }
    }

    /// <summary>
    /// Implements elapsed-time measurement stages used by the timed stream operations.
    /// </summary>
    internal static class Timed
    {
        /// <summary>
        /// Holds the stopwatch shared by a graph's start and stop stages. The context is created while the graph is assembled, so reusing that graph also reuses this stopwatch.
        /// </summary>
        internal sealed class TimedFlowContext
        {
            private readonly Stopwatch _stopwatch = new();

            /// <summary>
            /// Starts measuring elapsed time.
            /// </summary>
            public void Start() => _stopwatch.Start();

            /// <summary>
            /// Stops measuring elapsed time and returns the accumulated duration.
            /// </summary>
            /// <returns>The elapsed time recorded by this context's stopwatch.</returns>
            public TimeSpan Stop()
            {
                _stopwatch.Stop();
                return _stopwatch.Elapsed;
            }
        }

        /// <summary>
        /// Starts the shared timer when the first element passes through the stage.
        /// </summary>
        /// <typeparam name="T">The element type passed through the stage.</typeparam>
        internal sealed class StartTimed<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly StartTimed<T> _stage;
                private bool _started;

                public Logic(StartTimed<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.Outlet, this);
                    SetHandler(stage.Inlet, this);
                }

                public override void OnPush()
                {
                    if (!_started)
                    {
                        _stage._timedContext.Start();
                        _started = true;
                    }

                    Push(_stage.Outlet, Grab(_stage.Inlet));
                }

                public override void OnPull() => Pull(_stage.Inlet);
            }

            #endregion  

            private readonly TimedFlowContext _timedContext;

            /// <summary>
            /// Creates a stage that starts the supplied timer context when it receives its first element.
            /// </summary>
            /// <param name="timedContext">The timer context shared with the stage that reports termination.</param>
            public StartTimed(TimedFlowContext timedContext)
            {
                _timedContext = timedContext;
            }

            /// <summary>
            /// Creates the pass-through logic for this timer-start stage.
            /// </summary>
            /// <param name="inheritedAttributes">Attributes inherited during materialization.</param>
            /// <returns>The stage logic that starts the timer on the first element.</returns>
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        /// <summary>
        /// Reports the shared timer's elapsed time when the stream completes or fails, while passing elements through.
        /// </summary>
        /// <typeparam name="T">The element type passed through the stage.</typeparam>
        internal sealed class StopTime<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly StopTime<T> _stage;

                public Logic(StopTime<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, this);
                    SetHandler(stage.Inlet, this);
                }

                public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

                public override void OnUpstreamFinish()
                {
                    StopTime();
                    CompleteStage();
                }

                public override void OnUpstreamFailure(Exception e)
                {
                    StopTime();
                    FailStage(e);
                }

                public override void OnPull() => Pull(_stage.Inlet);
                
                private void StopTime()
                {
                    var d = _stage._timedContext.Stop();
                    _stage._onComplete(d);
                }
            }

            #endregion  

            private readonly TimedFlowContext _timedContext;
            private readonly Action<TimeSpan> _onComplete;

            /// <summary>
            /// Creates a stage that reports the elapsed time from the supplied context on upstream completion or failure.
            /// </summary>
            /// <param name="timedContext">The timer context shared with the stage that starts measurement.</param>
            /// <param name="onComplete">The callback that receives the elapsed time.</param>
            public StopTime(TimedFlowContext timedContext, Action<TimeSpan> onComplete)
            {
                _timedContext = timedContext;
                _onComplete = onComplete;
            }

            /// <summary>
            /// Creates the pass-through logic for this timer-stop stage.
            /// </summary>
            /// <param name="inheritedAttributes">Attributes inherited during materialization.</param>
            /// <returns>The stage logic that reports the elapsed time on termination.</returns>
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        /// <summary>
        /// Reports elapsed intervals between consecutive elements selected by a predicate.
        /// </summary>
        /// <typeparam name="T">The element type passed through the stage.</typeparam>
        internal sealed class TimedInterval<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly TimedInterval<T> _stage;
                private long _previousTicks;
                private long _matched;

                public Logic(TimedInterval<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, this);
                    SetHandler(stage.Inlet, this);
                }

                public override void OnPush()
                {
                    var element = Grab(_stage.Inlet);
                    if (_stage._matching(element))
                    {
                        var d = UpdateInterval();
                        if (_matched > 1)
                            _stage._onInterval(d);
                    }

                    Push(_stage.Outlet, element);
                }

                public override void OnPull() => Pull(_stage.Inlet);

                private TimeSpan UpdateInterval()
                {
                    _matched += 1;
                    var nowTicks = DateTime.Now.Ticks;
                    var d = nowTicks - _previousTicks;
                    _previousTicks = nowTicks;
                    return TimeSpan.FromTicks(d);
                }
            }

            #endregion  
            
            private readonly Func<T, bool> _matching;
            private readonly Action<TimeSpan> _onInterval;

            /// <summary>
            /// Creates a stage that measures intervals between matching elements.
            /// </summary>
            /// <param name="matching">Selects the elements included in interval measurements.</param>
            /// <param name="onInterval">The callback that receives each interval after the first match.</param>
            public TimedInterval(Func<T, bool> matching, Action<TimeSpan> onInterval)
            {
                _matching = matching;
                _onInterval = onInterval;
            }

            /// <summary>
            /// Creates the pass-through logic for this interval-measurement stage.
            /// </summary>
            /// <param name="inheritedAttributes">Attributes inherited during materialization.</param>
            /// <returns>The stage logic that measures intervals between matching elements.</returns>
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}
