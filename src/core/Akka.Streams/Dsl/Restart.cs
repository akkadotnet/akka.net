//-----------------------------------------------------------------------
// <copyright file="Restart.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public static Source<T, NotUsed> WithBackoff<T>(Func<Source<T, NotUsed>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor) 
            => Source.FromGraph(new RestartWithBackoffSource<T>(sourceFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffSource<T> : GraphStage<SourceShape<T>>
    {
        public Func<Source<T, NotUsed>> SourceFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffSource(
            Func<Source<T, NotUsed>> sourceFactory,
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
            private readonly RestartWithBackoffSource<T> _stage;

            public Logic(RestartWithBackoffSource<T> stage, string name) 
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
        public static Sink<T, NotUsed> WithBackoff<T>(Func<Sink<T, NotUsed>> sinkFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor) 
            => Sink.FromGraph(new RestartWithBackoffSink<T>(sinkFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffSink<T> : GraphStage<SinkShape<T>>
    {
        public Func<Sink<T, NotUsed>> SinkFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffSink(
            Func<Sink<T, NotUsed>> sinkFactory,
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
            private readonly RestartWithBackoffSink<T> _stage;

            public Logic(RestartWithBackoffSink<T> stage, string name)
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
        public static Flow<TIn, TOut, NotUsed> WithBackoff<TIn, TOut>(Func<Flow<TIn, TOut, NotUsed>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
            => Flow.FromGraph(new RestartWithBackoffFlow<TIn, TOut>(flowFactory, minBackoff, maxBackoff, randomFactor));
    }

    internal sealed class RestartWithBackoffFlow<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        public Func<Flow<TIn, TOut, NotUsed>> FlowFactory { get; }
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }

        public RestartWithBackoffFlow(
            Func<Flow<TIn, TOut, NotUsed>> flowFactory,
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
            private readonly RestartWithBackoffFlow<TIn, TOut> _stage;
            private Tuple<SubSourceOutlet<TIn>, SubSinkInlet<TOut>> activeOutIn = null;

            public Logic(RestartWithBackoffFlow<TIn, TOut> stage, string name)
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
                activeOutIn = Tuple.Create(sourceOut, sinkIn);
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
                if (activeOutIn != null)
                {
                    var sourceOut = activeOutIn.Item1;
                    var sinkIn = activeOutIn.Item2;
                    if (!sourceOut.IsClosed)
                    {
                        sourceOut.Complete();
                    }
                    if (!sinkIn.IsClosed)
                    {
                        sinkIn.Cancel();
                    }
                    activeOutIn = null;
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

        private int restartCount = 0;
        private int resetDeadline;
        // This is effectively only used for flows, if either the main inlet or outlet of this stage finishes, then we
        // don't want to restart the sub inlet when it finishes, we just finish normally.
        private bool finishing = false;

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
                    if (finishing)
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
                    if (finishing)
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
                    finishing = true;
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
                    if (finishing)
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
                    finishing = true;
                    sourceOut.Complete();
                },
                onUpstreamFailure: ex =>
                {
                    finishing = true;
                    sourceOut.Fail(ex);
                });

            return sourceOut;
        }

        // TODO: implement it
        internal void OnCompleteOrFailure()
        {
            
        }

        // TODO: implement it
        protected internal override void OnTimer(object timerKey)
        {
            StartGraph();
            // resetDeadline = minBackoff.fromNow
        }

        // When the stage starts, start the source
        public override void PreStart() => StartGraph();
    }
}
