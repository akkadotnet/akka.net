//-----------------------------------------------------------------------
// <copyright file="IOSources.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;
using Akka.Util.Internal;
using Reactive.Streams;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous Source backed by the given file.
    /// </summary>
    internal sealed class FileSource : SourceModule<ReadOnlySequence<byte>, Task<IOResult>>
    {
        private readonly FileInfo _f;
        private readonly int _chunkSize;
        private readonly long _startPosition;

        /// <summary>
        /// Creates a file source module that reads byte sequences from a file.
        /// </summary>
        /// <param name="f">The file to read.</param>
        /// <param name="chunkSize">The number of bytes requested for each read.</param>
        /// <param name="startPosition">The byte position at which reading starts.</param>
        /// <param name="attributes">The attributes attached to this source module.</param>
        /// <param name="shape">The source shape that emits byte sequences.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="chunkSize"/> is not positive or <paramref name="startPosition"/> is negative.</exception>
        public FileSource(FileInfo f, int chunkSize, long startPosition, Attributes attributes, SourceShape<ReadOnlySequence<byte>> shape) : base(shape)
        {
            if(chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be > 0 (was {chunkSize})", nameof(chunkSize));
            if(startPosition < 0)
                throw new ArgumentException($"startPosition must be >= 0 (was {startPosition})", nameof(startPosition));

            _f = f;
            _chunkSize = chunkSize;
            _startPosition = startPosition;
            Attributes = attributes;

            Label = $"FileSource({f}, {chunkSize})";
        }

        /// <summary>
        /// The attributes attached to this source module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// The diagnostic label for this file source.
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to attach to the copy.</param>
        /// <returns>A file source module with the supplied attributes and an amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new FileSource(_f, _chunkSize, _startPosition, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates a copy of this module with a replacement shape.
        /// </summary>
        /// <param name="shape">The source shape for the copy.</param>
        /// <returns>A file source module with the supplied shape.</returns>
        protected override SourceModule<ReadOnlySequence<byte>, Task<IOResult>> NewInstance(SourceShape<ReadOnlySequence<byte>> shape)
            => new FileSource(_f, _chunkSize, _startPosition, Attributes, shape);

        /// <summary>
        /// Materializes the file publisher and returns its Reactive Streams publisher.
        /// </summary>
        /// <param name="context">The materialization context containing the materializer and effective attributes.</param>
        /// <param name="task">Receives the task completed with the number of bytes read or an I/O failure.</param>
        /// <returns>The publisher that emits chunks read from the file.</returns>
        public override IPublisher<ReadOnlySequence<byte>> Create(MaterializationContext context, out Task<IOResult> task)
        {
            // FIXME rewrite to be based on GraphStage rather than dangerous downcasts
            var materializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = materializer.EffectiveSettings(context.EffectiveAttributes);

            var ioResultPromise = TaskEx.NonBlockingTaskCompletionSource<IOResult>();
            var props = FilePublisher.Props(_f, ioResultPromise, _chunkSize, _startPosition, settings.InitialInputBufferSize, settings.MaxInputBufferSize);
            var dispatcher = context.EffectiveAttributes.GetAttribute(DefaultAttributes.IODispatcher.GetAttribute<ActorAttributes.Dispatcher>());
            var actorRef = materializer.ActorOf(context, props.WithDispatcher(dispatcher.Name));

            task = ioResultPromise.Task;
            return new ActorPublisherImpl<ReadOnlySequence<byte>>(actorRef);

        }
    }

    /// <summary>
    /// INTERNAL API
    /// Source backed by the given input stream.
    /// </summary>
    internal sealed class InputStreamSource : SourceModule<ReadOnlySequence<byte>, Task<IOResult>>
    {
        private readonly Func<Stream> _createInputStream;
        private readonly int _chunkSize;

        /// <summary>
        /// Creates a source module backed by a stream created at materialization time.
        /// </summary>
        /// <param name="createInputStream">Creates the input stream to read.</param>
        /// <param name="chunkSize">The number of bytes requested for each read.</param>
        /// <param name="attributes">The attributes attached to this source module.</param>
        /// <param name="shape">The source shape that emits byte sequences.</param>
        public InputStreamSource(Func<Stream> createInputStream, int chunkSize, Attributes attributes, SourceShape<ReadOnlySequence<byte>> shape) : base(shape)
        {
            _createInputStream = createInputStream;
            _chunkSize = chunkSize;
            Attributes = attributes;
        }

        /// <summary>
        /// The attributes attached to this source module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to attach to the copy.</param>
        /// <returns>An input-stream source module with the supplied attributes and an amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new InputStreamSource(_createInputStream, _chunkSize, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates a copy of this module with a replacement shape.
        /// </summary>
        /// <param name="shape">The source shape for the copy.</param>
        /// <returns>An input-stream source module with the supplied shape.</returns>
        protected override SourceModule<ReadOnlySequence<byte>, Task<IOResult>> NewInstance(SourceShape<ReadOnlySequence<byte>> shape)
            => new InputStreamSource(_createInputStream, _chunkSize, Attributes, shape);

        /// <summary>
        /// Creates the input stream and materializes a publisher that reads it.
        /// </summary>
        /// <param name="context">The materialization context containing the materializer and effective attributes.</param>
        /// <param name="task">Receives the task completed with the number of bytes read or the creation/read failure.</param>
        /// <returns>The publisher that emits chunks read from the created stream, or an error publisher if stream creation fails.</returns>
        public override IPublisher<ReadOnlySequence<byte>> Create(MaterializationContext context, out Task<IOResult> task)
        {
            var materializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var ioResultPromise = TaskEx.NonBlockingTaskCompletionSource<IOResult>();
            IPublisher<ReadOnlySequence<byte>> pub;
            
            try
            {
                // can throw, i.e. FileNotFound
                var inputStream = _createInputStream();
                var props = InputStreamPublisher
                    .Props(inputStream, ioResultPromise, _chunkSize)
                    .WithDispatcher(context
                        .EffectiveAttributes
                        .GetMandatoryAttribute<ActorAttributes.Dispatcher>()
                        .Name);
                var actorRef = materializer.ActorOf(context, props);
                pub = new ActorPublisherImpl<ReadOnlySequence<byte>>(actorRef);
            }
            catch (Exception ex)
            {
                ioResultPromise.TrySetException(ex);
                pub = new ErrorPublisher<ReadOnlySequence<byte>>(ex, Attributes.GetNameOrDefault("inputStreamSource"));
            }

            task = ioResultPromise.Task;
            return pub;
        }
    }
}
