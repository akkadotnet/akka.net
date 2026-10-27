//-----------------------------------------------------------------------
// <copyright file="IOSinks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class FileSink : SinkModule<ReadOnlySequence<byte>, Task<IOResult>>
    {
        private readonly FileInfo _f;
        private readonly long _startPosition;
        private readonly FileMode _fileMode;
        private readonly bool _autoFlush;
        private readonly FlushSignaler _flushSignaler;

        /// <summary>
        /// Creates a file sink module that writes incoming byte sequences.
        /// </summary>
        /// <param name="f">The file to write.</param>
        /// <param name="startPosition">The byte position at which writing starts.</param>
        /// <param name="fileMode">The mode used to open or create the file.</param>
        /// <param name="attributes">The attributes attached to this sink module.</param>
        /// <param name="shape">The sink shape that receives byte sequences.</param>
        /// <param name="autoFlush">Whether to flush after each received element.</param>
        /// <param name="flushSignaler">Optional signaler that can request a file flush.</param>
        public FileSink(FileInfo f, long startPosition, FileMode fileMode, Attributes attributes, SinkShape<ReadOnlySequence<byte>> shape, bool autoFlush, FlushSignaler flushSignaler) : base(shape)
        {
            _f = f;
            _startPosition = startPosition;
            _fileMode = fileMode;
            Attributes = attributes;
            _autoFlush = autoFlush;
            _flushSignaler = flushSignaler;

            Label = $"FileSink({f}, {fileMode})";
        }

        /// <summary>
        /// The attributes attached to this sink module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// The diagnostic label for this file sink.
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to attach to the copy.</param>
        /// <returns>A file sink module with the supplied attributes and an amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new FileSink(_f, _startPosition, _fileMode, attributes, AmendShape(attributes), _autoFlush, _flushSignaler);


        /// <summary>
        /// Creates a copy of this module with a replacement shape.
        /// </summary>
        /// <param name="shape">The sink shape for the copy.</param>
        /// <returns>A file sink module with the supplied shape.</returns>
        protected override SinkModule<ReadOnlySequence<byte>, Task<IOResult>> NewInstance(SinkShape<ReadOnlySequence<byte>> shape)
            => new FileSink(_f, _startPosition, _fileMode, Attributes, shape, _autoFlush, _flushSignaler);

        /// <summary>
        /// Materializes the file subscriber actor and returns its Reactive Streams subscriber.
        /// </summary>
        /// <param name="context">The materialization context containing the materializer and effective attributes.</param>
        /// <param name="materializer">Receives the task completed with the file write result.</param>
        /// <returns>The subscriber that forwards incoming elements to the file actor.</returns>
        public override object Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);

            var ioResultPromise = TaskEx.NonBlockingTaskCompletionSource<IOResult>();
            var props = FileSubscriber.Props(_f, ioResultPromise, settings.MaxInputBufferSize, _startPosition, _fileMode, _autoFlush, _flushSignaler);

            var actorRef = mat.ActorOf(
                context, 
                props.WithDispatcher(context
                    .EffectiveAttributes
                    .GetMandatoryAttribute<ActorAttributes.Dispatcher>()
                    .Name));
            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ReadOnlySequence<byte>>(actorRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous  Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class OutputStreamSink : SinkModule<ReadOnlySequence<byte>, Task<IOResult>>
    {
        private readonly Func<Stream> _createOutput;
        private readonly bool _autoFlush;

        /// <summary>
        /// Creates an output-stream sink module.
        /// </summary>
        /// <param name="createOutput">Creates the stream that receives the sink's bytes.</param>
        /// <param name="attributes">The attributes attached to this sink module.</param>
        /// <param name="shape">The sink shape that receives byte sequences.</param>
        /// <param name="autoFlush">Whether to flush after each received element.</param>
        public OutputStreamSink(Func<Stream> createOutput, Attributes attributes, SinkShape<ReadOnlySequence<byte>> shape, bool autoFlush) : base(shape)
        {
            _createOutput = createOutput;
            Attributes = attributes;
            _autoFlush = autoFlush;
        }

        /// <summary>
        /// The attributes attached to this sink module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to attach to the copy.</param>
        /// <returns>An output-stream sink module with the supplied attributes and an amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new OutputStreamSink(_createOutput, attributes, AmendShape(attributes), _autoFlush);

        /// <summary>
        /// Creates a copy of this module with a replacement shape.
        /// </summary>
        /// <param name="shape">The sink shape for the copy.</param>
        /// <returns>An output-stream sink module with the supplied shape.</returns>
        protected override SinkModule<ReadOnlySequence<byte>, Task<IOResult>> NewInstance(SinkShape<ReadOnlySequence<byte>> shape)
            => new OutputStreamSink(_createOutput, Attributes, shape, _autoFlush);

        /// <summary>
        /// Creates the output stream, materializes its subscriber actor, and returns the subscriber.
        /// </summary>
        /// <param name="context">The materialization context containing the materializer and effective attributes.</param>
        /// <param name="materializer">Receives the task completed with the output-stream write result.</param>
        /// <returns>The subscriber that forwards incoming elements to the output-stream actor.</returns>
        public override object Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);
            var ioResultPromise = TaskEx.NonBlockingTaskCompletionSource<IOResult>();

            var os = _createOutput();
            var maxInputBufferSize = context
                .EffectiveAttributes
                .GetMandatoryAttribute<Attributes.InputBuffer>()
                .Max;
            var props = OutputStreamSubscriber
                .Props(os, ioResultPromise, maxInputBufferSize, _autoFlush)
                .WithDispatcher(context
                    .EffectiveAttributes
                    .GetMandatoryAttribute<ActorAttributes.Dispatcher>()
                    .Name);
            var actorRef = mat.ActorOf(context, props);

            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ReadOnlySequence<byte>>(actorRef);
        }
    }
}
