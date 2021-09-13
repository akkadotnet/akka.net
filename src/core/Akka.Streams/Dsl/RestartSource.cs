// //-----------------------------------------------------------------------
// // <copyright file="RestartSource.cs" company="Akka.NET Project">
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
        /// This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Source<T, NotUsed> WithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor);
            return WithBackoff(sourceFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// This <see cref="Source"/> will never emit a complete or failure, since the completion or failure of the wrapped <see cref="Source"/>
        /// is always handled by restarting it. The wrapped <see cref="Source"/> can however be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxRestarts">The amount of restarts is capped to this amount within a time frame of minBackoff. Passing `0` will cause no restarts and a negative number will not cap the amount of restarts.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Source<T, NotUsed> WithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor).WithMaxRestarts(maxRestarts, minBackoff);
            return WithBackoff(sourceFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails or complete using an exponential
        /// backoff.
        /// <para>
        /// This <see cref="Source"/> will never emit a complete or failure, since the completion or failure of the wrapped <see cref="Source"/>
        /// is always handled by restarting it. The wrapped <see cref="Source"/> can however be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="settings"><see cref="RestartSettings" /> defining restart configuration</param>
        public static Source<T, NotUsed> WithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, RestartSettings settings)
            => Source.FromGraph(new RestartWithBackoffSource<T, TMat>(sourceFactory, settings, onlyOnFailures: false));

        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails using an exponential backoff.
        /// This <see cref="Source"/> will never emit a failure, since the failure of the wrapped <see cref="Source"/> is always handled by
        /// restarting. The wrapped <see cref="Source"/> can be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Source<T, NotUsed> OnFailuresWithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor);
            return OnFailuresWithBackoff(sourceFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails using an exponential backoff.
        /// This <see cref="Source"/> will never emit a failure, since the failure of the wrapped <see cref="Source"/> is always handled by
        /// restarting. The wrapped <see cref="Source"/> can be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="minBackoff">Minimum (initial) duration until the child actor will started again, if it is terminated</param>
        /// <param name="maxBackoff">The exponential back-off is capped to this duration</param>
        /// <param name="randomFactor">After calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="maxRestarts">The amount of restarts is capped to this amount within a time frame of minBackoff. Passing `0` will cause no restarts and a negative number will not cap the amount of restarts.</param>
        [Obsolete("Use the overloaded method which accepts Akka.Stream.RestartSettings instead.")]
        public static Source<T, NotUsed> OnFailuresWithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts)
        {
            var settings = RestartSettings.Create(minBackoff, maxBackoff, randomFactor).WithMaxRestarts(maxRestarts, minBackoff);
            return OnFailuresWithBackoff(sourceFactory, settings);
        }

        /// <summary>
        /// Wrap the given <see cref="Source"/> with a <see cref="Source"/> that will restart it when it fails using an exponential backoff.
        /// <para>
        /// This <see cref="Source"/> will never emit a failure, since the failure of the wrapped <see cref="Source"/> is always handled by
        /// restarting. The wrapped <see cref="Source"/> can be cancelled by cancelling this <see cref="Source"/>.
        /// When that happens, the wrapped <see cref="Source"/>, if currently running will be cancelled, and it will not be restarted.
        /// This can be triggered simply by the downstream cancelling, or externally by introducing a <see cref="IKillSwitch"/> right
        /// after this <see cref="Source"/> in the graph.
        /// </para>
        /// <para>This uses the same exponential backoff algorithm as <see cref="BackoffOptions"/>.</para>
        /// </summary>
        /// <param name="sourceFactory">A factory for producing the <see cref="Source"/> to wrap.</param>
        /// <param name="settings"><see cref="RestartSettings" /> defining restart configuration</param>
        public static Source<T, NotUsed> OnFailuresWithBackoff<T, TMat>(Func<Source<T, TMat>> sourceFactory, RestartSettings settings)
            => Source.FromGraph(new RestartWithBackoffSource<T, TMat>(sourceFactory, settings, onlyOnFailures: true));
    }
    
    internal sealed class RestartWithBackoffSource<T, TMat> : GraphStage<SourceShape<T>>
    {
        public Func<Source<T, TMat>> SourceFactory { get; }
        public RestartSettings Settings { get; }
        public bool OnlyOnFailures { get; }

        public RestartWithBackoffSource(Func<Source<T, TMat>> sourceFactory, RestartSettings settings, bool onlyOnFailures)
        {
            SourceFactory = sourceFactory;
            Settings = settings;
            OnlyOnFailures = onlyOnFailures;
            Shape = new SourceShape<T>(Out);
        }

        public Outlet<T> Out { get; } = new Outlet<T>("RestartWithBackoffSource.out");

        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes, "Source");

        private sealed class Logic : RestartWithBackoffLogic<SourceShape<T>, T, T>
        {
            private readonly RestartWithBackoffSource<T, TMat> _stage;
            private readonly Attributes _inheritedAttributes;

            public Logic(RestartWithBackoffSource<T, TMat> stage, Attributes inheritedAttributes, string name)
                : base(name, stage.Shape, null, stage.Out, stage.Settings, stage.OnlyOnFailures)
            {
                _stage = stage;
                _inheritedAttributes = inheritedAttributes;
                Backoff();
            }

            protected override void StartGraph()
            {
                var sinkIn = CreateSubInlet(_stage.Out);
                SubFusingMaterializer.Materialize(_stage.SourceFactory().To(sinkIn.Sink), _inheritedAttributes);
                if (IsAvailable(_stage.Out))
                    sinkIn.Pull();
            }

            protected override void Backoff() => SetHandler(_stage.Out, () =>
            {
                // do nothing
            });
        }
    }

}