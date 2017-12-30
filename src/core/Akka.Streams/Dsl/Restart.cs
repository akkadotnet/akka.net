﻿//-----------------------------------------------------------------------
// <copyright file="Restart.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A RestartSource wraps a <see cref="Source"/> that gets restarted when it completes or fails.
    /// They are useful for graphs that need to run for longer than the <see cref="Source"/> can necessarily guarantee it will, for
    /// example, for <see cref="Source"/> streams that depend on a remote server that may crash or become partitioned. The
    /// RestartSource ensures that the graph can continue running while the <see cref="Source"/> restarts.
    /// </summary>
    public static class RestartSource
    {
        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// This <see cref="Source"/> will never emit a complete or failure, since the completion or failure of the wrapped <see cref="Source"/>
        /// is always handled by restarting it. The wrapped <see cref="Source"/> can however be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// This uses the same exponential backoff algorithm as <see cref="Akka.Pattern.Backoff"/>.
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        public static Source<T, NotUsed> WithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor) 
            => Source.FromGraph(new RestartWithBackoffSource<T, TMat>(sourceFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffSource<T, TMat> : GraphStage<SourceShape<T>>
    {
        public Func<Source<T, TMat>> SourceFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffSource(
            Func<Source<T, TMat>> sourceFactory,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor)
        {
            SourceFactory = sourceFactory;
            MinBackoff = minBackoff;
            MaxBackoff = maxBackoff;
            RandomFactor = randomFactor;
            Shape = new SourceShape<T>(Out);
        }

        public Outlet<T> Out { get; } = new Outlet<T>("RestartWithBackoffSource.out");
        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this, "Source");
        }

        private class Logic : RestartWithBackoffLogic<SourceShape<T>>
        {
            private readonly RestartWithBackoffSource<T, TMat> _stage;

            public Logic(RestartWithBackoffSource<T, TMat> stage, string name) 
                : base(name, stage.Shape, stage.MinBackoff, stage.MaxBackoff, stage.RandomFactor)
            {
                _stage = stage;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sinkIn = CreateSubInlet(_stage.Out);
                _stage.SourceFactory().RunWith(sinkIn.Sink, SubFusingMaterializer);
                if (IsAvailable(_stage.Out))
                {
                    sinkIn.Pull();
                }
            }

            protected override void Backoff()
            {
                SetHandler(_stage.Out, () =>
                {
                    // do nothing
                });
            }
        }
    }

    /// <summary>
    /// A RestartSink wraps a <see cref="Sink"/> that gets restarted when it completes or fails.
    /// They are useful for graphs that need to run for longer than the <see cref="Sink"/> can necessarily guarantee it will, for
    /// example, for <see cref="Sink"/> streams that depend on a remote server that may crash or become partitioned. The
    /// RestartSink ensures that the graph can continue running while the <see cref="Sink"/> restarts.
    /// </summary>
    public static class RestartSink
    {
        /// <summary>
        /// Wrap the given <see cref="Sink"/> with a <see cref="Sink"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// This <see cref="Sink"/> will never cancel, since cancellation by the wrapped <see cref="Sink"/> is always handled by restarting it.
        /// The wrapped <see cref="Sink"/> can however be completed by feeding a completion or error into this <see cref="Sink"/>. When that
        /// happens, the <see cref="Sink"/>, if currently running, will terminate and will not be restarted. This can be triggered
        /// simply by the upstream completing, or externally by introducing a <see cref="IKillSwitch"/> right before this <see cref="Sink"/> in the
        /// graph.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. When the wrapped <see cref="Sink"/> does cancel, this <see cref="Sink"/> will backpressure, however any elements already
        /// sent may have been lost.
        /// This uses the same exponential backoff algorithm as <see cref="Akka.Pattern.Backoff"/>.
        /// </summary>
        /// <param name="sinkFactory">A factory for producing the <see cref="Sink"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        public static Sink<T, NotUsed> WithBackoff<T, TMat>(Func<Sink<T, TMat>> sinkFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor) 
            => Sink.FromGraph(new RestartWithBackoffSink<T, TMat>(sinkFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffSink<T, TMat> : GraphStage<SinkShape<T>>
    {
        public Func<Sink<T, TMat>> SinkFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffSink(
            Func<Sink<T, TMat>> sinkFactory,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor)
        {
            SinkFactory = sinkFactory;
            MinBackoff = minBackoff;
            MaxBackoff = maxBackoff;
            RandomFactor = randomFactor;
            Shape = new SinkShape<T>(In);
        }

        public Inlet<T> In { get; } = new Inlet<T>("RestartWithBackoffSink.in");
        public override SinkShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this, "Sink");
        }

        private class Logic : RestartWithBackoffLogic<SinkShape<T>>
        {
            private readonly RestartWithBackoffSink<T, TMat> _stage;

            public Logic(RestartWithBackoffSink<T, TMat> stage, string name)
                : base(name, stage.Shape, stage.MinBackoff, stage.MaxBackoff, stage.RandomFactor)
            {
                _stage = stage;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sourceOut = CreateSubOutlet(_stage.In);
                Source.FromGraph(sourceOut.Source).RunWith(_stage.SinkFactory(), SubFusingMaterializer);
            }

            protected override void Backoff()
            {
                SetHandler(_stage.In, () =>
                {
                    // do nothing
                });
            }
        }
    }

    /// <summary>
    /// A RestartFlow wraps a <see cref="Flow"/> that gets restarted when it completes or fails.
    /// They are useful for graphs that need to run for longer than the <see cref="Flow"/> can necessarily guarantee it will, for
    /// example, for <see cref="Flow"/> streams that depend on a remote server that may crash or become partitioned. The
    /// RestartFlow ensures that the graph can continue running while the <see cref="Flow"/> restarts.
    /// </summary>
    public static class RestartFlow
    {
        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// This <see cref="Flow"/> will not cancel, complete or emit a failure, until the opposite end of it has been cancelled or
        /// completed.Any termination by the <see cref="Flow"/> before that time will be handled by restarting it. Any termination
        /// signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's running, and then the <see cref="Flow"/>
        /// will be allowed to terminate without being restarted.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// and any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure.
        /// This uses the same exponential backoff algorithm as <see cref="Akka.Pattern.Backoff"/>.
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        public static Flow<TIn, TOut, NotUsed> WithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
            => Flow.FromGraph(new RestartWithBackoffFlow<TIn, TOut, TMat>(flowFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffFlow<TIn, TOut, TMat> : GraphStage<FlowShape<TIn, TOut>>
    {
        public Func<Flow<TIn, TOut, TMat>> FlowFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffFlow(
            Func<Flow<TIn, TOut, TMat>> flowFactory,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor)
        {
            FlowFactory = flowFactory;
            MinBackoff = minBackoff;
            MaxBackoff = maxBackoff;
            RandomFactor = randomFactor;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        public Inlet<TIn> In { get; } = new Inlet<TIn>("RestartWithBackoffFlow.in");
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("RestartWithBackoffFlow.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this, "Flow");
        }

        private class Logic : RestartWithBackoffLogic<FlowShape<TIn, TOut>>
        {
            private readonly RestartWithBackoffFlow<TIn, TOut, TMat> _stage;
            private Tuple<SubSourceOutlet<TIn>, SubSinkInlet<TOut>> _activeOutIn;

            public Logic(RestartWithBackoffFlow<TIn, TOut, TMat> stage, string name)
                : base(name, stage.Shape, stage.MinBackoff, stage.MaxBackoff, stage.RandomFactor)
            {
                _stage = stage;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sourceOut = CreateSubOutlet(_stage.In);
                var sinkIn = CreateSubInlet(_stage.Out);
                Source.FromGraph(sourceOut.Source).Via(_stage.FlowFactory()).RunWith(sinkIn.Sink, SubFusingMaterializer);
                if (IsAvailable(_stage.Out))
                {
                    sinkIn.Pull();
                }
                _activeOutIn = Tuple.Create(sourceOut, sinkIn);
            }

            protected override void Backoff()
            {
                SetHandler(_stage.In, () =>
                {
                    // do nothing
                });
                SetHandler(_stage.Out, () =>
                {
                    // do nothing
                });

                // We need to ensure that the other end of the sub flow is also completed, so that we don't
                // receive any callbacks from it.
                if (_activeOutIn != null)
                {
                    var sourceOut = _activeOutIn.Item1;
                    var sinkIn = _activeOutIn.Item2;
                    if (!sourceOut.IsClosed)
                    {
                        sourceOut.Complete();
                    }
                    if (!sinkIn.IsClosed)
                    {
                        sinkIn.Cancel();
                    }
                    _activeOutIn = null;
                }
            }
        }
    }

    /// <summary>
    /// Shared logic for all restart with backoff logics.
    /// </summary>
    internal abstract class RestartWithBackoffLogic<S> : TimerGraphStageLogic where S : Shape
    {
        private readonly string _name;
        private readonly S _shape;
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;

        protected Inlet In { get; }
        protected Outlet Out { get; }

        private int _restartCount;
        private Deadline _resetDeadline;
        // This is effectively only used for flows, if either the main inlet or outlet of this stage finishes, then we
        // don't want to restart the sub inlet when it finishes, we just finish normally.
        private bool _finishing;

        protected RestartWithBackoffLogic(
            string name,
            S shape,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            double randomFactor) : base(shape)
        {
            _name = name;
            _shape = shape;
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;

            _resetDeadline = minBackoff.FromNow();

            In = shape.Inlets.FirstOrDefault();
            Out = shape.Outlets.FirstOrDefault();
        }

        protected abstract void StartGraph();
        protected abstract void Backoff();

        protected SubSinkInlet<T> CreateSubInlet<T>(Outlet<T> outlet)
        {
            var sinkIn = new SubSinkInlet<T>(this, $"RestartWithBackoff{_name}.subIn");

            sinkIn.SetHandler(new LambdaInHandler(
                onPush: () =>
                {
                    Push(Out, sinkIn.Grab());
                },
                onUpstreamFinish: () =>
                {
                    if (_finishing)
                    {
                        Complete(Out);
                    }
                    else
                    {
                        Log.Debug("Graph out finished");
                        OnCompleteOrFailure();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    if (_finishing)
                    {
                        Fail(_shape.Outlets.First(), ex);
                    }
                    else
                    {
                        Log.Error(ex, "Restarting graph due to failure");
                        OnCompleteOrFailure();
                    }
                }));

            SetHandler(Out, 
                onPull: () => sinkIn.Pull(),
                onDownstreamFinish: () =>
                {
                    _finishing = true;
                    sinkIn.Cancel();
                });
            
            return sinkIn;
        }

        protected SubSourceOutlet<T> CreateSubOutlet<T>(Inlet<T> inlet)
        {
            var sourceOut = new SubSourceOutlet<T>(this, $"RestartWithBackoff{_name}.subOut");
            sourceOut.SetHandler(new LambdaOutHandler(
                onPull: () =>
                {
                    if (IsAvailable(In))
                    {
                        sourceOut.Push(Grab<T>(In));
                    }
                    else
                    {
                        if (!HasBeenPulled(In))
                            Pull(In);
                    }
                },
                onDownstreamFinish: () =>
                {
                    if (_finishing)
                    {
                        Cancel(In);
                    }
                    else
                    {
                        Log.Debug("Graph in finished");
                        OnCompleteOrFailure();
                    }
                }
            ));

            SetHandler(In, 
                onPush: () =>
                {
                    if (sourceOut.IsAvailable)
                        sourceOut.Push(Grab<T>(In));
                },
                onUpstreamFinish: () =>
                {
                    _finishing = true;
                    sourceOut.Complete();
                },
                onUpstreamFailure: ex =>
                {
                    _finishing = true;
                    sourceOut.Fail(ex);
                });

            return sourceOut;
        }

        internal void OnCompleteOrFailure()
        {
            // Check if the last start attempt was more than the minimum backoff
            if (_resetDeadline.IsOverdue)
            {
                Log.Debug($"Last restart attempt was more than {_minBackoff} ago, resetting restart count");
                _restartCount = 0;
            }

            var restartDelay = BackoffSupervisor.CalculateDelay(_restartCount, _minBackoff, _maxBackoff, _randomFactor);
            Log.Debug($"Restarting graph in {restartDelay}");
            ScheduleOnce("RestartTimer", restartDelay);
            _restartCount += 1;
            // And while we wait, we go into backoff mode
            Backoff();
        }

        protected internal override void OnTimer(object timerKey)
        {
            StartGraph();
            _resetDeadline = _minBackoff.FromNow();
        }

        // When the stage starts, start the source
        public override void PreStart() => StartGraph();
    }

    internal sealed class Deadline
    {
        public Deadline(TimeSpan time)
        {
            Time = time;
        }

        public TimeSpan Time { get; }

        public bool IsOverdue => Time.Ticks - DateTime.UtcNow.Ticks < 0;

        public static Deadline Now => new Deadline(new TimeSpan(DateTime.UtcNow.Ticks));

        public static Deadline operator +(Deadline deadline, TimeSpan duration)
        {
            return new Deadline(deadline.Time.Add(duration));
        }
    }

    internal static class DeadlineExtensions
    {
        public static Deadline FromNow(this TimeSpan timespan)
        {
            return Deadline.Now + timespan;
        }
    }
}
