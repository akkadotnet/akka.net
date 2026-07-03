//-----------------------------------------------------------------------
// <copyright file="ChannelDrainSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Custom "drain-many" source over an externally-owned <see cref="ChannelReader{T}"/> —
    /// the prototype of the Artery <c>SendQueue</c> replacement from design.md Decision 9.
    ///
    /// <para>
    /// Hot path: while the channel has elements, every pull is served by a synchronous
    /// <see cref="ChannelReader{T}.TryRead"/> inside the interpreter island — zero mailbox
    /// hops per element. Only the empty→non-empty transition costs one async wakeup
    /// (coalesced: a single wakeup resumes the drain loop for however many elements have
    /// accumulated). Contrast with stock <c>Source.Queue</c>, which pays one mailbox hop
    /// per offer (verified in design.md Decision 2) — quantified head-to-head in
    /// <c>ArteryIngressSourceBenchmarks</c>.
    /// </para>
    ///
    /// <para>
    /// The channel is externally owned, mirroring Artery's requirement that the outbound
    /// queue survive stream restarts (an Association-owned queue re-attached by a new
    /// consumer materialization). Requires a single-reader channel: this stage must be the
    /// only consumer.
    /// </para>
    /// </summary>
    public sealed class ChannelDrainSource<T> : GraphStage<SourceShape<T>>
    {
        private readonly ChannelReader<T> _reader;

        public ChannelDrainSource(ChannelReader<T> reader)
        {
            _reader = reader;
        }

        public Outlet<T> Out { get; } = new("ChannelDrainSource.out");

        public override SourceShape<T> Shape => new(Out);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly ChannelDrainSource<T> _stage;
            private readonly Action<bool> _onWakeup;

            public Logic(ChannelDrainSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _onWakeup = GetAsyncCallback<bool>(OnWakeup);
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                if (_stage._reader.TryRead(out var element))
                {
                    Push(_stage.Out, element);
                    return;
                }

                ArmWakeup();
            }

            private void ArmWakeup()
            {
                while (true)
                {
                    var wait = _stage._reader.WaitToReadAsync();
                    if (wait.IsCompletedSuccessfully)
                    {
                        if (!wait.Result)
                        {
                            CompleteStage();
                            return;
                        }

                        // Raced with the producer: an element landed between TryRead and
                        // WaitToReadAsync. Single reader ⇒ it is still there for us.
                        if (_stage._reader.TryRead(out var element))
                        {
                            Push(_stage.Out, element);
                            return;
                        }

                        continue;
                    }

                    // Cold path: channel is empty. One async wakeup resumes the drain loop.
                    wait.AsTask().ContinueWith(
                        t => _onWakeup(t.Status == TaskStatus.RanToCompletion && t.Result),
                        TaskContinuationOptions.ExecuteSynchronously);
                    return;
                }
            }

            private void OnWakeup(bool canRead)
            {
                if (!canRead)
                {
                    CompleteStage();
                    return;
                }

                if (!IsAvailable(_stage.Out))
                    return; // wakeup only armed under a pending pull; defensive guard

                if (_stage._reader.TryRead(out var element))
                    Push(_stage.Out, element);
                else
                    ArmWakeup();
            }
        }
    }
}
