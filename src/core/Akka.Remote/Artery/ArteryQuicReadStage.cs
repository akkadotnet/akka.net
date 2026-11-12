// -----------------------------------------------------------------------
// <copyright file="ArteryQuicReadStage.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://dotnetfoundation.org/>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>Reads on demand without transferring scratch storage or native stream ownership.</summary>
    internal sealed class ArteryQuicReadStage : GraphStage<SourceShape<ReadOnlySequence<byte>>>
    {
        private readonly Stream _stream;
        private readonly CancellationToken _lifetime;
        private readonly ArteryQuicIoCounters? _counters;
        private readonly Action _onEof;

        internal ArteryQuicReadStage(Stream stream, CancellationToken lifetime,
            ArteryQuicIoCounters? counters, Action onEof)
        {
            _stream = stream;
            _lifetime = lifetime;
            _counters = counters;
            _onEof = onEof;
            Shape = new SourceShape<ReadOnlySequence<byte>>(new Outlet<ReadOnlySequence<byte>>("ArteryQuic.read"));
        }

        public override SourceShape<ReadOnlySequence<byte>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly ArteryQuicReadStage _stage;
            private readonly byte[] _scratch = GC.AllocateUninitializedArray<byte>(16 * 1024, pinned: true);
            private Action<int>? _read;
            private Action<Exception>? _failed;

            internal Logic(ArteryQuicReadStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Shape.Outlet, this);
            }

            public override void PreStart()
            {
                _read = GetAsyncCallback<int>(Publish);
                _failed = GetAsyncCallback<Exception>(FailStage);
            }

            public override void OnPull()
            {
                try
                {
                    var read = _stage._stream.ReadAsync(_scratch, _stage._lifetime);
                    if (read.IsCompletedSuccessfully)
                        Publish(read.GetAwaiter().GetResult());
                    else
                        _ = AwaitReadAsync(read);
                }
                catch (Exception error)
                {
                    FailStage(error);
                }
            }

            private async Task AwaitReadAsync(ValueTask<int> read)
            {
                // Consume completion even after downstream cancellation. This operation keeps
                // its private scratch alive until native use settles; the connection owns disposal.
                try
                {
                    _read!(await read.ConfigureAwait(false));
                }
                catch (Exception error)
                {
                    _failed!(error);
                }
            }

            private void Publish(int count)
            {
                if (count == 0)
                {
                    _stage._onEof();
                    CompleteStage();
                    return;
                }

                _stage._counters?.Read(count);
                // Actors and framing can retain this chunk indefinitely. Only scratch is reused.
                Push(_stage.Shape.Outlet, new ReadOnlySequence<byte>(_scratch.AsSpan(0, count).ToArray()));
            }
        }
    }
}
