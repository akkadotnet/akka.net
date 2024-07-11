//-----------------------------------------------------------------------
// <copyright file="ChannelSinks.cs" company="Akka.NET Project">
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
    internal sealed class ChannelSinkLogic<T> : InGraphStageLogic
    {
        private readonly Inlet<T> _inlet;
        private readonly ChannelWriter<T> _writer;
        private readonly Action<bool> _onWriteAvailable;
        private readonly Action<Exception> _onWriteFailed;
        private readonly Action<Task<bool>> _onWriteReady;
        private T _awaitingElement;
        private readonly bool _isOwner;

        public ChannelSinkLogic(SinkShape<T> shape, Inlet<T> inlet,
            ChannelWriter<T> writer, bool isOwner) : base(shape)
        {
            _inlet = inlet;
            _writer = writer;
            _isOwner = isOwner;
            _onWriteAvailable = GetAsyncCallback<bool>(OnWriteAvailable);
            _onWriteFailed = GetAsyncCallback<Exception>(OnWriteFailed);
            _onWriteReady = OnWriteReady;
            SetHandler(_inlet, this);
        }

        private void OnWriteFailed(Exception cause) => TryFail(cause);

        private void OnWriteAvailable(bool available)
        {
            if (available && _writer.TryWrite(_awaitingElement))
                Pull(_inlet);
            else
                TryComplete();
        }

        private void TryComplete()
        {
            CompleteStage();
            if (_isOwner)
                _writer.TryComplete();
        }

        private void TryFail(Exception e)
        {
            FailStage(e);
            if (_isOwner)
                _writer.TryComplete(e);
        }

        public override void OnUpstreamFinish()
        {
            base.OnUpstreamFinish();
            if (_isOwner)
                _writer.TryComplete();
        }

        public override void OnUpstreamFailure(Exception e)
        {
            base.OnUpstreamFailure(e);
            if (_isOwner)
                _writer.TryComplete(e);
        }

        public override void PreStart()
        {
            Pull(_inlet);
            base.PreStart();
        }

        public override void OnPush()
        {
            var element = Grab(_inlet);
            if (_writer.TryWrite(element))
            {
                Pull(_inlet);
            }
            else
            {
                var continuation = _writer.WaitToWriteAsync();
                if (continuation.IsCompletedSuccessfully)
                {
                    var available = continuation.GetAwaiter().GetResult();
                    if (available && _writer.TryWrite(element))
                    {
                        Pull(_inlet);
                    }
                    else
                    {
                        TryComplete();
                    }
                }
                else
                {
                    var task = continuation.AsTask();
                    _awaitingElement = element;
                    task.ContinueWith(_onWriteReady);
                }
            }

        }

        private void OnWriteReady(Task<bool> t)
        {
            if (t.IsFaulted) _onWriteFailed(t.Exception);
            else if (t.IsCanceled)
                _onWriteFailed(new TaskCanceledException(t));
            else _onWriteAvailable(t.Result);
        }
    }
    
    internal sealed class ChannelReaderSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, ChannelReader<T>>
    {
        private readonly int _bufferSize;
        private readonly bool _singleReader;
        private readonly BoundedChannelFullMode _fullMode;

        public ChannelReaderSink(int bufferSize, bool singleReader = false, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        {
            _bufferSize = bufferSize;
            _singleReader = singleReader;
            _fullMode = fullMode;
            Inlet = new Inlet<T>("channelReader.in");
            Shape = new SinkShape<T>(Inlet);
        }

        public Inlet<T> Inlet { get; }
        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<ChannelReader<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_bufferSize)
            {
                SingleWriter = true,
                AllowSynchronousContinuations = false,
                SingleReader = _singleReader,
                FullMode = _fullMode
            });
            return new LogicAndMaterializedValue<ChannelReader<T>>(new ChannelSinkLogic<T>(this.Shape, this.Inlet, channel.Writer, true), channel);
        }
    }
    
    internal sealed class ChannelWriterSink<T> : GraphStage<SinkShape<T>>
    {
        private readonly ChannelWriter<T> _writer;
        private readonly bool _isOwner;

        public ChannelWriterSink(ChannelWriter<T> writer, bool isOwner)
        {
            _writer = writer;
            _isOwner = isOwner;
            Inlet = new Inlet<T>("channelReader.in");
            Shape = new SinkShape<T>(Inlet);
        }

        public Inlet<T> Inlet { get; }
        public override SinkShape<T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new ChannelSinkLogic<T>(this.Shape, this.Inlet, _writer, _isOwner);
    }
}
