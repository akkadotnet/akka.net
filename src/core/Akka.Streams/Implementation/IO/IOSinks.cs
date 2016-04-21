//-----------------------------------------------------------------------
// <copyright file="IOSinks.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class FileSink : SinkModule<ByteString, Task<IOResult>>
    {
        private readonly FileInfo _f;
        private readonly FileMode _fileMode;
        private readonly Attributes _attributes;

        public FileSink(FileInfo f, FileMode fileMode, Attributes attributes, SinkShape<ByteString> shape) : base(shape)
        {
            _f = f;
            _fileMode = fileMode;
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new FileSink(_f, _fileMode, attributes, AmendShape(attributes));
        

        protected override SinkModule<ByteString, Task<IOResult>> NewInstance(SinkShape<ByteString> shape)
            => new FileSink(_f, _fileMode, Attributes, shape);

        public override ISubscriber<ByteString> Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializer.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);

            var ioResultPromise = new TaskCompletionSource<IOResult>();
            var props = FileSubscriber.Props(_f, ioResultPromise, settings.MaxInputBufferSize, _fileMode);
            var dispatcher = context.EffectiveAttributes.GetAttribute(DefaultAttributes.IODispatcher.AttributeList.First()) as ActorAttributes.Dispatcher;

            var actorRef = mat.ActorOf(context, props.WithDispatcher(dispatcher.Name));
            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ByteString>(actorRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous  Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class OutputStreamSink : SinkModule<ByteString, Task<IOResult>>
    {
        private readonly Func<Stream> _createOutput;
        private readonly Attributes _attributes;
        private readonly bool _autoFlush;

        public OutputStreamSink(Func<Stream> createOutput, Attributes attributes, SinkShape<ByteString> shape, bool autoFlush) : base(shape)
        {
            _createOutput = createOutput;
            _attributes = attributes;
            _autoFlush = autoFlush;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new OutputStreamSink(_createOutput, attributes, AmendShape(attributes), _autoFlush);
        
        protected override SinkModule<ByteString, Task<IOResult>> NewInstance(SinkShape<ByteString> shape)
            => new OutputStreamSink(_createOutput, _attributes, shape, _autoFlush);

        public override ISubscriber<ByteString> Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializer.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);
            var ioResultPromise = new TaskCompletionSource<IOResult>();

            var os = _createOutput();
            var props = OutputStreamSubscriber.Props(os, ioResultPromise, settings.MaxInputBufferSize, _autoFlush);
            var actorRef = mat.ActorOf(context, props);

            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ByteString>(actorRef);
        }
    }
}
