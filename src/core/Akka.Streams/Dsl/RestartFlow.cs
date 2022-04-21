//-----------------------------------------------------------------------
// <copyright file="Restart.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Pattern;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
  
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
        /// <para>
        /// This <see cref="Flow"/> will not cancel, complete or emit a failure, until the opposite end of it has been cancelled or
        /// completed.Any termination by the <see cref="Flow"/> before that time will be handled by restarting it. Any termination
        /// signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's running, and then the <see cref="Flow"/>
        /// will be allowed to terminate without being restarted.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// and any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Flow<TIn, TOut, NotUsed> WithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor);
            return WithBackoff(flowFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// <para>
        /// This <see cref="Flow"/> will not cancel, complete or emit a failure, until the opposite end of it has been cancelled or
        /// completed.Any termination by the <see cref="Flow"/> before that time will be handled by restarting it. Any termination
        /// signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's running, and then the <see cref="Flow"/>
        /// will be allowed to terminate without being restarted.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// and any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxRestarts">The amount of restarts is capped to this amount within a time frame of minBackoff. Passing `0` will cause no restarts and a negative number will not cap the amount of restarts.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Flow<TIn, TOut, NotUsed> WithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor).WithMaxRestarts(maxRestarts, minBackoff);
            return WithBackoff(flowFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// <para>
        /// This <see cref="Flow"/> will not cancel, complete or emit a failure, until the opposite end of it has been cancelled or
        /// completed.Any termination by the <see cref="Flow"/> before that time will be handled by restarting it. Any termination
        /// signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's running, and then the <see cref="Flow"/>
        /// will be allowed to terminate without being restarted.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// and any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="settings"><see cref="RestartSettings" /> defining restart configuration</param>
        public static Flow<TIn, TOut, NotUsed> WithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, RestartSettings settings)
            => Flow.FromGraph(new RestartWithBackoffFlow<TIn, TOut, TMat>(flowFactory, settings, onlyOnFailures: false));

        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails using an exponential
        /// backoff. Notice that this <see cref="Flow"/> will not restart on completion of the wrapped flow. 
        /// <para>
        /// This <see cref="Flow"/> will not emit any failure
        /// The failures by the wrapped <see cref="Flow"/> will be handled by
        /// restarting the wrapping <see cref="Flow"/> as long as maxRestarts is not reached.
        /// Any termination signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's
        /// running, and then the <see cref="Flow"/> will be allowed to terminate without being restarted. 
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// nd any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure. 
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Flow<TIn, TOut, NotUsed> OnFailuresWithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor);
            return OnFailuresWithBackoff(flowFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails using an exponential
        /// backoff. Notice that this <see cref="Flow"/> will not restart on completion of the wrapped flow. 
        /// <para>
        /// This <see cref="Flow"/> will not emit any failure
        /// The failures by the wrapped <see cref="Flow"/> will be handled by
        /// restarting the wrapping <see cref="Flow"/> as long as maxRestarts is not reached.
        /// Any termination signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's
        /// running, and then the <see cref="Flow"/> will be allowed to terminate without being restarted. 
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// nd any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure. 
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxRestarts">The amount of restarts is capped to this amount within a time frame of minBackoff. Passing `0` will cause no restarts and a negative number will not cap the amount of restarts.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Flow<TIn, TOut, NotUsed> OnFailuresWithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor).WithMaxRestarts(maxRestarts, minBackoff);
            return OnFailuresWithBackoff(flowFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Flow"/> with a <see cref="Flow"/> that will restart it when it fails using an exponential
        /// backoff. Notice that this <see cref="Flow"/> will not restart on completion of the wrapped flow. 
        /// <para>
        /// This <see cref="Flow"/> will not emit any failure
        /// The failures by the wrapped <see cref="Flow"/> will be handled by
        /// restarting the wrapping <see cref="Flow"/> as long as maxRestarts is not reached.
        /// Any termination signals sent to this <see cref="Flow"/> however will terminate the wrapped <see cref="Flow"/>, if it's
        /// running, and then the <see cref="Flow"/> will be allowed to terminate without being restarted. 
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. A termination signal from either end of the wrapped <see cref="Flow"/> will cause the other end to be terminated,
        /// nd any in transit messages will be lost. During backoff, this <see cref="Flow"/> will backpressure. 
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="flowFactory">A factory for producing the <see cref="Flow"/>] to wrap.</param>
        public static Flow<TIn, TOut, NotUsed> OnFailuresWithBackoff<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, RestartSettings settings)
            => Flow.FromGraph(new RestartWithBackoffFlow<TIn, TOut, TMat>(flowFactory, settings, onlyOnFailures: true));
    }

    internal sealed class RestartWithBackoffFlow<TIn, TOut, TMat> : GraphStage<FlowShape<TIn, TOut>>
    {
        public Func<Flow<TIn, TOut, TMat>> FlowFactory { get; }
        public RestartSettings Settings { get; }
        public bool OnlyOnFailures { get; }
        
        public RestartWithBackoffFlow(
            Func<Flow<TIn, TOut, TMat>> flowFactory,
            RestartSettings settings,
            bool onlyOnFailures)
        {
            FlowFactory = flowFactory;
            Settings = settings;
            OnlyOnFailures = onlyOnFailures;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        public Inlet<TIn> In { get; } = new Inlet<TIn>("RestartWithBackoffFlow.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("RestartWithBackoffFlow.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes, "Flow");

        private sealed class Logic : RestartWithBackoffLogic<FlowShape<TIn, TOut>, TIn, TOut>
        {
            private readonly RestartWithBackoffFlow<TIn, TOut, TMat> _stage;
            private readonly Attributes _inheritedAttributes;
            private Tuple<SubSourceOutlet<TIn>, SubSinkInlet<TOut>> _activeOutIn;
            private TimeSpan _delay;
            
            public Logic(RestartWithBackoffFlow<TIn, TOut, TMat> stage, Attributes inheritedAttributes, string name)
                : base(name, stage.Shape, stage.In, stage.Out, stage.Settings, stage.OnlyOnFailures)
            {
                _inheritedAttributes = inheritedAttributes;
                _delay = _inheritedAttributes.GetAttribute<RestartWithBackoffFlow.Delay>(new RestartWithBackoffFlow.Delay(TimeSpan.FromMilliseconds(50))).Duration;
                _stage = stage;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sourceOut = CreateSubOutlet(_stage.In);
                var sinkIn = CreateSubInlet(_stage.Out);
                
                var graph = Source.FromGraph(sourceOut.Source)
                    //temp fix becaues the proper fix would be to have a concept of cause of cancellation. See https://github.com/akka/akka/pull/23909
                    //TODO register issue to track this
                    .Via(DelayCancellation<TIn>(_delay))
                    .Via(_stage.FlowFactory())
                    .To(sinkIn.Sink);
                SubFusingMaterializer.Materialize(graph, _inheritedAttributes);
               
                if (IsAvailable(_stage.Out))
                    sinkIn.Pull();

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
                        sourceOut.Complete();

                    if (!sinkIn.IsClosed)
                        sinkIn.Cancel();
                    _activeOutIn = null;
                }
            }
            
            private Flow<T, T, NotUsed> DelayCancellation<T>(TimeSpan duration) => Flow.FromGraph(new DelayCancellationStage<T>(duration, null));
        }
    }

    /// <summary>
    /// Shared logic for all restart with backoff logics.
    /// </summary>
    internal abstract class RestartWithBackoffLogic<TShape, TIn, TOut> : TimerGraphStageLogic where TShape : Shape
    {
        private readonly string _name;
        private readonly RestartSettings _settings;
        private readonly bool _onlyOnFailures;
        
        protected Inlet<TIn> In { get; }
        protected Outlet<TOut> Out { get; }

        private int _restartCount;
        private Deadline _resetDeadline;

        // This is effectively only used for flows, if either the main inlet or outlet of this stage finishes, then we
        // don't want to restart the sub inlet when it finishes, we just finish normally.
        private bool _finishing;

        protected RestartWithBackoffLogic(
            string name,
            TShape shape,
            Inlet<TIn> inlet,
            Outlet<TOut> outlet,
            RestartSettings settings,
            bool onlyOnFailures) : base(shape)
        {
            _name = name;
            _settings = settings;
            _onlyOnFailures = onlyOnFailures;

            _resetDeadline = settings.MaxRestartsWithin.FromNow();

            In = inlet;
            Out = outlet;
        }

        protected abstract void StartGraph();

        protected abstract void Backoff();

        protected SubSinkInlet<TOut> CreateSubInlet(Outlet<TOut> outlet)
        {
            var sinkIn = new SubSinkInlet<TOut>(this, $"RestartWithBackoff{_name}.subIn");

            sinkIn.SetHandler(new LambdaInHandler(
                onPush: () => Push(Out, sinkIn.Grab()),
                onUpstreamFinish: () =>
                {
                    if (_finishing || MaxRestartsReached() || _onlyOnFailures)
                        Complete(Out);
                    else
                    {
                        ScheduleRestartTimer();
                    }
                },
                /*
                 * upstream in this context is the wrapped stage
                 */
                onUpstreamFailure: ex =>
                {
                    if (_finishing || MaxRestartsReached())
                        Fail(Out, ex);
                    else
                    {
                        Log.Warning(ex, "Restarting graph due to failure.");
                        ScheduleRestartTimer();
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

        protected SubSourceOutlet<TIn> CreateSubOutlet(Inlet<TIn> inlet)
        {
            var sourceOut = new SubSourceOutlet<TIn>(this, $"RestartWithBackoff{_name}.subOut");
            sourceOut.SetHandler(new LambdaOutHandler(
                onPull: () =>
                {
                    if (IsAvailable(In))
                        sourceOut.Push(Grab(In));
                    else
                    {
                        if (!HasBeenPulled(In))
                            Pull(In);
                    }
                },
                onDownstreamFinish: () =>
                {
                    if (_finishing || MaxRestartsReached() || _onlyOnFailures)
                        Cancel(In);
                    else
                    {
                        ScheduleRestartTimer();
                    }
                }
            ));

            SetHandler(In,
                onPush: () =>
                {
                    if (sourceOut.IsAvailable)
                        sourceOut.Push(Grab(In));
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

        internal bool MaxRestartsReached()
        {
            // Check if the last start attempt was more than the reset deadline
            if (_resetDeadline.IsOverdue)
            {
                Log.Debug("Last restart attempt was more than {0} ago, resetting restart count", _settings.MaxRestartsWithin);
                _restartCount = 0;
            }
            return _restartCount == _settings.MaxRestarts;
        }

        /// <summary>
        /// Set a timer to restart after the calculated delay
        /// </summary>
        internal void ScheduleRestartTimer()
        {
            var restartDelay = BackoffSupervisor.CalculateDelay(_restartCount, _settings.MinBackoff, _settings.MaxBackoff, _settings.RandomFactor);
            Log.Debug("Restarting graph in {0}", restartDelay);
            ScheduleOnce("RestartTimer", restartDelay);
            _restartCount += 1;
            // And while we wait, we go into backoff mode
            Backoff();
        }

        /// <summary>
        /// Invoked when the backoff timer ticks
        /// </summary>
        protected internal override void OnTimer(object timerKey)
        {
            StartGraph();
            _resetDeadline = _settings.MaxRestartsWithin.FromNow();
        }

        /// <summary>
        /// When the stage starts, start the source
        /// </summary>
        public override void PreStart() => StartGraph();
    }
    
    public class RestartWithBackoffFlow {
    /// <summary>
    /// Temporary attribute that can override the time a [[RestartWithBackoffFlow]] waits
    /// for a failure before cancelling.
    /// See https://github.com/akka/akka/issues/24529
    /// Should be removed if/when cancellation can include a cause.
    /// </summary>
    public class Delay : Attributes.IAttribute, IEquatable<Delay>
    {
        /// <summary>
        /// Delay duration
        /// </summary>
        public readonly TimeSpan Duration;

        public Delay(TimeSpan duration)
        {
            Duration = duration;
        }
            
        
        public bool Equals(Delay other) => !ReferenceEquals(other, null) && Equals(Duration, other.Duration);

       
        public override bool Equals(object obj) => obj is Delay && Equals((Delay)obj);

       
        public override int GetHashCode() => Duration.GetHashCode();

       
        public override string ToString() => $"Duration({Duration})";
    }
    }
    
    /// <summary>
    /// Returns a flow that is almost identical but delays propagation of cancellation from downstream to upstream.
    /// Once the down stream is finished calls to onPush are ignored
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class DelayCancellationStage<T> : SimpleLinearGraphStage<T>
    {
        private readonly TimeSpan _delay;

        public DelayCancellationStage(TimeSpan delay, string name = null) : base(name)
        {
            _delay = delay;
        }
        
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
        
        private sealed class Logic : TimerGraphStageLogic
        {
            private readonly DelayCancellationStage<T> _stage;
            
            public Logic(DelayCancellationStage<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                
                SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));

                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet), onDownstreamFinish: OnDownStreamFinished );
            }
            
            /// <summary>
            /// We should really. port the Cause parameter functionality for the OnDownStreamFinished delegate
            /// </summary>
            private void OnDownStreamFinished()
            {
                //_cause = new Option<Exception>(/*cause*/);
                ScheduleOnce("CompleteState", _stage._delay);
                SetHandler(_stage.Inlet, onPush:DoNothing);
            }

            protected internal override void OnTimer(object timerKey)
            {
                Log.Debug($"Stage was cancelled after delay of {_stage._delay}");
                CompleteStage();
                
                // this code will replace the CompleteStage() call once we port the Exception Cause parameter for the OnDownStreamFinished delegate
                /*if(_cause != null)
                    FailStage(_cause.Value); //<-- is this the same as cancelStage ?
                else
                {
                    throw new IllegalStateException("Timer hitting without first getting a cancel cannot happen");
                }*/
            }
        }
    }
    
    internal sealed class Deadline
    {
        public Deadline(TimeSpan time) => Time = time;

        public TimeSpan Time { get; }

        public bool IsOverdue => Time.Ticks - DateTime.UtcNow.Ticks < 0;

        public static Deadline Now => new Deadline(new TimeSpan(DateTime.UtcNow.Ticks));

        public static Deadline operator +(Deadline deadline, TimeSpan duration) => new Deadline(deadline.Time.Add(duration));
    }

    internal static class DeadlineExtensions
    {
        public static Deadline FromNow(this TimeSpan timespan) => Deadline.Now + timespan;
    }
}
