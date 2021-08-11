// //-----------------------------------------------------------------------
// // <copyright file="RestartSink.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Pattern;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
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
        /// <para>
        /// This <see cref="Sink"/> will never cancel, since cancellation by the wrapped <see cref="Sink"/> is always handled by restarting it.
        /// The wrapped <see cref="Sink"/> can however be completed by feeding a completion or error into this <see cref="Sink"/>. When that
        /// happens, the <see cref="Sink"/>, if currently running, will terminate and will not be restarted. This can be triggered
        /// simply by the upstream completing, or externally by introducing a <see cref="IKillSwitch"/> right before this <see cref="Sink"/> in the
        /// graph.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. When the wrapped <see cref="Sink"/> does cancel, this <see cref="Sink"/> will backpressure, however any elements already
        /// sent may have been lost.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="sinkFactory">A factory for producing the <see cref="Sink"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Sink<T, NotUsed> WithBackoff<T, TMat>(Func<Sink<T, TMat>> sinkFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor);
            return WithBackoff(sinkFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Sink"/> with a <see cref="Sink"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// <para>
        /// This <see cref="Sink"/> will never cancel, since cancellation by the wrapped <see cref="Sink"/> is always handled by restarting it.
        /// The wrapped <see cref="Sink"/> can however be completed by feeding a completion or error into this <see cref="Sink"/>. When that
        /// happens, the <see cref="Sink"/>, if currently running, will terminate and will not be restarted. This can be triggered
        /// simply by the upstream completing, or externally by introducing a <see cref="IKillSwitch"/> right before this <see cref="Sink"/> in the
        /// graph.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. When the wrapped <see cref="Sink"/> does cancel, this <see cref="Sink"/> will backpressure, however any elements already
        /// sent may have been lost.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="sinkFactory">A factory for producing the <see cref="Sink"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxRestarts">The amount of restarts is capped to this amount within a time frame of minBackoff. Passing `0` will cause no restarts and a negative number will not cap the amount of restarts.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Sink<T, NotUsed> WithBackoff<T, TMat>(Func<Sink<T, TMat>> sinkFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor).WithMaxRestarts(maxRestarts, minBackoff);
            return WithBackoff(sinkFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Sink"/> with a <see cref="Sink"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// <para>
        /// This <see cref="Sink"/> will never cancel, since cancellation by the wrapped <see cref="Sink"/> is always handled by restarting it.
        /// The wrapped <see cref="Sink"/> can however be completed by feeding a completion or error into this <see cref="Sink"/>. When that
        /// happens, the <see cref="Sink"/>, if currently running, will terminate and will not be restarted. This can be triggered
        /// simply by the upstream completing, or externally by introducing a <see cref="IKillSwitch"/> right before this <see cref="Sink"/> in the
        /// graph.
        /// The restart process is inherently lossy, since there is no coordination between cancelling and the sending of
        /// messages. When the wrapped <see cref="Sink"/> does cancel, this <see cref="Sink"/> will backpressure, however any elements already
        /// sent may have been lost.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="sinkFactory">A factory for producing the <see cref="Sink"/> to wrap.</param>
        /// <param name="settings"><see cref="RestartSettings" /> defining restart configuration</param>
        public static Sink<T, NotUsed> WithBackoff<T, TMat>(Func<Sink<T, TMat>> sinkFactory, RestartSettings settings)
            => Sink.FromGraph(new RestartWithBackoffSink<T, TMat>(sinkFactory, settings));
    }
    
    internal sealed class RestartWithBackoffSink<T, TMat> : GraphStage<SinkShape<T>>
    {
        public Func<Sink<T, TMat>> SinkFactory { get; }
        public RestartSettings Settings { get; }

        public RestartWithBackoffSink(Func<Sink<T, TMat>> sinkFactory, RestartSettings settings)
        {
            SinkFactory = sinkFactory;
            Settings = settings;
            Shape = new SinkShape<T>(In);
        }

        public Inlet<T> In { get; } = new Inlet<T>("RestartWithBackoffSink.in");

        public override SinkShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes,"Sink");

        private sealed class Logic : RestartWithBackoffLogic<SinkShape<T>, T, T>
        {
            private readonly RestartWithBackoffSink<T, TMat> _stage;
            private readonly Attributes _inheritedAttributes;

            public Logic(RestartWithBackoffSink<T, TMat> stage, Attributes inheritedAttributes, string name)
                : base(name, stage.Shape, stage.In, null, stage.Settings, onlyOnFailures: false)
            {
                _stage = stage;
                _inheritedAttributes = inheritedAttributes;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sourceOut = CreateSubOutlet(_stage.In);
                SubFusingMaterializer.Materialize(Source.FromGraph(sourceOut.Source).To(_stage.SinkFactory()), _inheritedAttributes);
            }

            protected override void Backoff() => SetHandler(_stage.In, () =>
            {
                // do nothing
            });
        }
    }

}