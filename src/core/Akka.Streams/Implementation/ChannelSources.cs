//-----------------------------------------------------------------------
// <copyright file="ChannelSources.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    sealed class ChannelSourceLogic<T> : OutGraphStageLogic
    {
        private readonly Outlet<T> _outlet;
        private readonly ChannelReader<T> _reader;
        private readonly Action<bool> _onValueRead;
        private readonly Action<Exception> _onValueReadFailure;
        private readonly Action<Exception> _onReaderComplete;
        private readonly Action<Task<bool>> _onReadReady;

        public ChannelSourceLogic(SourceShape<T> source, Outlet<T> outlet,
            ChannelReader<T> reader) : base(source)
        {
            _outlet = outlet;
            _reader = reader;
            _onValueRead = GetAsyncCallback<bool>(OnValueRead);
            _onValueReadFailure =
                GetAsyncCallback<Exception>(OnValueReadFailure);
            _onReaderComplete = GetAsyncCallback<Exception>(OnReaderComplete);
            _onReadReady = ContinueAsyncRead;
            _reader.Completion.ContinueWith(t =>
            {
                if (t.IsFaulted) _onReaderComplete(t.Exception);
                else if (t.IsCanceled)
                    _onReaderComplete(new TaskCanceledException(t));
                else _onReaderComplete(null);
            });

            SetHandler(_outlet, this);
        }

        private void OnReaderComplete(Exception reason)
        {
            if (reason is null)
                CompleteStage();
            else
                FailStage(reason);
        }

        private void OnValueReadFailure(Exception reason) => FailStage(reason);

        private void OnValueRead(bool dataAvailable)
        {
            if (dataAvailable && _reader.TryRead(out var element))
                Push(_outlet, element);
            else
                CompleteStage();
        }

        public override void OnPull()
        {
            if (_reader.TryRead(out var element))
            {
                Push(_outlet, element);
            }
            else
            {
                var continuation = _reader.WaitToReadAsync();
                if (continuation.IsCompletedSuccessfully)
                {
                    var dataAvailable = continuation.GetAwaiter().GetResult();
                    if (dataAvailable && _reader.TryRead(out element))
                        Push(_outlet, element);
                    else
                        CompleteStage();
                }
                else
                    continuation.AsTask().ContinueWith(_onReadReady);
            }
        }

        private void ContinueAsyncRead(Task<bool> t)
        {
            if (t.IsFaulted)
                _onValueReadFailure(t.Exception);
            else if (t.IsCanceled)
                _onValueReadFailure(new TaskCanceledException(t));
            else
                _onValueRead(t.Result);
        }
    }

    internal sealed class ChannelReaderWithMaterializedWriterSource<T> :
        GraphStageWithMaterializedValue<SourceShape<T>, ChannelWriter<T>>
    {
        private readonly int _bufferSize;
        private readonly bool _singleWriter;
        private readonly BoundedChannelFullMode _fullMode;

        public ChannelReaderWithMaterializedWriterSource(int bufferSize,
            bool singleWriter = false,
            BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        {
            _bufferSize = bufferSize;
            _singleWriter = singleWriter;
            _fullMode = fullMode;
            Outlet = new Outlet<T>("channelReader.out");
            Shape = new SourceShape<T>(Outlet);
        }

        public Outlet<T> Outlet { get; }
        public override SourceShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<ChannelWriter<T>>
            CreateLogicAndMaterializedValue(
                Attributes inheritedAttributes)
        {

            var channel = Channel.CreateBounded<T>(
                new BoundedChannelOptions(_bufferSize)
                {
                    SingleWriter = _singleWriter,
                    AllowSynchronousContinuations = false,
                    SingleReader = true,
                    FullMode = _fullMode
                });
            return new LogicAndMaterializedValue<ChannelWriter<T>>(
                new ChannelSourceLogic<T>(this.Shape, this.Outlet,
                    channel.Reader), channel);
        }
    }

    internal sealed class ChannelReaderSource<T> : GraphStage<SourceShape<T>>
    {

        private readonly ChannelReader<T> _reader;

        public ChannelReaderSource(ChannelReader<T> reader)
        {
            _reader = reader;
            Outlet = new Outlet<T>("channelReader.out");
            Shape = new SourceShape<T>(Outlet);
        }

        public Outlet<T> Outlet { get; }
        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic
            CreateLogic(Attributes inheritedAttributes) =>
            new ChannelSourceLogic<T>(Shape, Outlet, _reader);
    }
}
