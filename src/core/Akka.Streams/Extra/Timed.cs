//-----------------------------------------------------------------------
// <copyright file="Timed.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
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
        /// Measures time from receiving the first element and completion events - one for each subscriber of this <see cref="IFlow{TOut,TMat}"/>.
        /// </summary>
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
        /// Measures time from receiving the first element and completion events - one for each subscriber of this <see cref="IFlow{TOut,TMat}"/>.
        /// </summary>
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
        public static IFlow<TIn, TMat> TimedIntervalBetween<TIn, TMat>(IFlow<TIn, TMat> flow, Func<TIn, bool> matching, Action<TimeSpan> onInterval)
        {
            var timedInterval =
                Flow.Create<TIn>()
                    .Via(new TimedIntervall<TIn>(matching, onInterval))
                    .Named("timedInterval");

            return flow.Via(timedInterval);
        }
    }

    internal static class Timed
    {
        internal sealed class TimedFlowContext
        {
            private readonly Stopwatch _stopwatch = new Stopwatch();

            public void Start() => _stopwatch.Start();

            public TimeSpan Stop()
            {
                _stopwatch.Stop();
                return _stopwatch.Elapsed;
            }
        }

        internal sealed class StartTimed<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : GraphStageLogic
            {
                public Logic(StartTimed<T> stage) : base(stage.Shape)
                {
                    var started = false;
                    SetHandler(stage.Outlet, onPull:()=> Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: () =>
                    {
                        if (!started)
                        {
                            stage._timedContext.Start();
                            started = true;
                        }

                        Push(stage.Outlet, Grab(stage.Inlet));
                    });
                }
            }

            #endregion  

            private readonly TimedFlowContext _timedContext;

            public StartTimed(TimedFlowContext timedContext)
            {
                _timedContext = timedContext;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        internal sealed class StopTime<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : GraphStageLogic
            {
                private readonly StopTime<T> _stage;

                public Logic(StopTime<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)),
                        onUpstreamFailure: ex =>
                        {
                            StopTime();
                            FailStage(ex);
                        },
                        onUpstreamFinish: () =>
                        {
                            StopTime();
                            CompleteStage();
                        });
                }

                private void StopTime()
                {
                    var d = _stage._timedContext.Stop();
                    _stage._onComplete(d);
                }
            }

            #endregion  

            private readonly TimedFlowContext _timedContext;
            private readonly Action<TimeSpan> _onComplete;

            public StopTime(TimedFlowContext timedContext, Action<TimeSpan> onComplete)
            {
                _timedContext = timedContext;
                _onComplete = onComplete;
            }
            
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        internal sealed class TimedIntervall<T> : SimpleLinearGraphStage<T>
        {
            #region Loigc 

            private sealed class Logic : GraphStageLogic
            {
                private long _previousTicks;
                private long _matched;

                public Logic(TimedIntervall<T> stage) : base(stage.Shape)
                {
                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: () =>
                        {
                            var element = Grab(stage.Inlet);
                            if (stage._matching(element))
                            {
                                var d = UpdateInterval();
                                if (_matched > 1)
                                    stage._onInterval(d);
                            }

                            Push(stage.Outlet, element);
                        });
                }

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
            
            public TimedIntervall(Func<T, bool> matching, Action<TimeSpan> onInterval)
            {
                _matching = matching;
                _onInterval = onInterval;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}