using System;
using System.Diagnostics;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using static Akka.Streams.Extra.Timed;

namespace Akka.Streams.Extra
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Provides operations needed to implement the `timed` DSL
    /// </summary>
    internal static class TimedOps
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// Measures time from receiving the first element and completion events - one for each subscriber of this `Flow`.
        /// </summary>
        public static Source<TOut, TMat2> Timed<TIn, TOut, TMat, TMat2>(Source<TIn, TMat> source, Func<Source<TIn, TMat>, Source<TOut, TMat2>> measuredOps, Action<TimeSpan> onComplete)
        {
            var ctx = new TimedFlowContext();

            var startTimed = Flow.Create<TIn>().Transform(() => new StartTimed<TIn>(ctx)).Named("startTimed");
            var stopTimed = Flow.Create<TOut>().Transform(() => new StopTime<TOut>(ctx, onComplete)).Named("stopTimed");

            return measuredOps(source.Via(startTimed)).Via(stopTimed);
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Measures time from receiving the first element and completion events - one for each subscriber of this `Flow`.
        /// </summary>
        public static Flow<TIn, TOut2, TMat2> Timed<TIn, TOut, TOut2, TMat, TMat2>(Flow<TIn, TOut, TMat> flow, Func<Flow<TIn, TOut, TMat>, Flow<TIn, TOut2, TMat2>> measuredOps, Action<TimeSpan> onComplete)
        {
            // todo is there any other way to provide this for Flow, without duplicating impl?
            // they do share a super-type (FlowOps), but all operations of FlowOps return path dependant type
            var ctx = new TimedFlowContext();

            var startTimed = Flow.Create<TOut>().Transform(() => new StartTimed<TOut>(ctx)).Named("startTimed");
            var stopTimed = Flow.Create<TOut2>().Transform(() => new StopTime<TOut2>(ctx, onComplete)).Named("stopTimed");

            return measuredOps(flow.Via(startTimed)).Via(stopTimed);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Provides operations needed to implement the `timedIntervalBetween` DSL
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
                    .Transform(() => new TimedIntervall<TIn>(matching, onInterval))
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

        internal sealed class StartTimed<T> : PushStage<T, T>
        {
            private readonly TimedFlowContext _timedContext;
            private bool _started;

            public StartTimed(TimedFlowContext timedContext)
            {
                _timedContext = timedContext;
            }

            public override ISyncDirective OnPush(T element, IContext<T> context)
            {
                if (!_started)
                {
                    _timedContext.Start();
                    _started = true;
                }

                return context.Push(element);
            }
        }

        internal sealed class StopTime<T> : PushStage<T, T>
        {
            private readonly TimedFlowContext _timedContext;
            private readonly Action<TimeSpan> _onComplete;

            public StopTime(TimedFlowContext timedContext, Action<TimeSpan> onComplete)
            {
                _timedContext = timedContext;
                _onComplete = onComplete;
            }

            public override ISyncDirective OnPush(T element, IContext<T> context) => context.Push(element);

            public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<T> context)
            {
                Complete();
                return context.Fail(cause);
            }

            public override ITerminationDirective OnUpstreamFinish(IContext<T> context)
            {
                Complete();
                return context.Finish();
            }

            private void Complete() => _onComplete(_timedContext.Stop());
        }

        internal sealed class TimedIntervall<T> : PushStage<T, T>
        {
            private readonly Func<T, bool> _matching;
            private readonly Action<TimeSpan> _onInterval;
            private long _prevTicks;
            private long _matched;

            public TimedIntervall(Func<T, bool> matching, Action<TimeSpan> onInterval)
            {
                _matching = matching;
                _onInterval = onInterval;
            }

            public override ISyncDirective OnPush(T element, IContext<T> context)
            {
                if (_matching(element))
                {
                    var d = UpdateInterval(element);

                    if (_matched > 1)
                        _onInterval(d);
                }

                return context.Push(element);
            }

            private TimeSpan UpdateInterval(T elem)
            {
                _matched++;
                var nowTicks = DateTime.Now.Ticks;
                var d = nowTicks - _prevTicks;
                _prevTicks = nowTicks;
                return TimeSpan.FromTicks(d);
            }
        }
    }
}