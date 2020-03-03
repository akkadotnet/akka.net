//-----------------------------------------------------------------------
// <copyright file="IOSources.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;
using Reactive.Streams;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous Source backed by the given file.
    /// </summary>
    internal sealed class FileSource : SourceModule<ByteString, Task<IOResult>>
    {
        private readonly FileInfo _f;
        private readonly int _chunkSize;
        private readonly long _startPosition;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="chunkSize">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public FileSource(FileInfo f, int chunkSize, long startPosition, Attributes attributes, SourceShape<ByteString> shape) : base(shape)
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
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new FileSource(_f, _chunkSize, _startPosition, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<ByteString, Task<IOResult>> NewInstance(SourceShape<ByteString> shape)
            => new FileSource(_f, _chunkSize, _startPosition, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public override IPublisher<ByteString> Create(MaterializationContext context, out Task<IOResult> task)
        {
            // FIXME rewrite to be based on GraphStage rather than dangerous downcasts
            var materializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = materializer.EffectiveSettings(context.EffectiveAttributes);

            var ioResultPromise = new TaskCompletionSource<IOResult>();
            var props = FilePublisher.Props(_f, ioResultPromise, _chunkSize, _startPosition, settings.InitialInputBufferSize, settings.MaxInputBufferSize);
            var dispatcher = context.EffectiveAttributes.GetAttribute(DefaultAttributes.IODispatcher.GetAttribute<ActorAttributes.Dispatcher>());
            var actorRef = materializer.ActorOf(context, props.WithDispatcher(dispatcher.Name));

            task = ioResultPromise.Task;
            return new ActorPublisherImpl<ByteString>(actorRef);

        }
    }

    /// <summary>
    /// INTERNAL API
    /// Source backed by the given input stream.
    /// </summary>
    internal sealed class InputStreamSource : SourceModule<ByteString, Task<IOResult>>
    {
        private readonly Func<Stream> _createInputStream;
        private readonly int _chunkSize;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="createInputStream">TBD</param>
        /// <param name="chunkSize">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public InputStreamSource(Func<Stream> createInputStream, int chunkSize, Attributes attributes, SourceShape<ByteString> shape) : base(shape)
        {
            _createInputStream = createInputStream;
            _chunkSize = chunkSize;
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new InputStreamSource(_createInputStream, _chunkSize, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<ByteString, Task<IOResult>> NewInstance(SourceShape<ByteString> shape)
            => new InputStreamSource(_createInputStream, _chunkSize, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public override IPublisher<ByteString> Create(MaterializationContext context, out Task<IOResult> task)
        {
            var materializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var ioResultPromise = new TaskCompletionSource<IOResult>();
            IPublisher<ByteString> pub;
            
            try
            {
                // can throw, i.e. FileNotFound
                var inputStream = _createInputStream();
                var props = InputStreamPublisher.Props(inputStream, ioResultPromise, _chunkSize);
                var actorRef = materializer.ActorOf(context, props);
                pub = new ActorPublisherImpl<ByteString>(actorRef);
            }
            catch (Exception ex)
            {
                ioResultPromise.TrySetException(ex);
                pub = new ErrorPublisher<ByteString>(ex, Attributes.GetNameOrDefault("inputStreamSource"));
            }

            task = ioResultPromise.Task;
            return pub;
        }
    }
}
