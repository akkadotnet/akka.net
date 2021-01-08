//-----------------------------------------------------------------------
// <copyright file="StreamConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Implementation.IO;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Converters for interacting with the IO streams APIs
    /// </summary>
    public static class StreamConverters
    {
        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> from an <see cref="Stream"/> created by the given function.
        /// Emitted elements are <paramref name="chunkSize"/> sized <see cref="ByteString"/> elements.
        /// The actual size of emitted elements depends how much data the underlying
        /// <see cref="Stream"/> returns on each read invocation. Such chunks will
        /// never be larger than chunkSize though.
        /// <para>
        /// You can configure the default dispatcher for this Source by changing the "akka.stream.blocking-io-dispatcher" or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>.
        /// </para>
        /// <para>
        /// It materializes a <see cref="Task{TResult}"/> of <see cref="IOResult"/> containing the number of bytes read from the source file upon completion,
        /// and a possible exception if IO operation was not completed successfully.
        /// </para>
        /// The created <see cref="Stream"/> will be closed when the <see cref="Source{TOut,TMat}"/> is cancelled.
        /// </summary>
        /// <param name="createInputStream">A function which creates the <see cref="Stream"/> to read from</param>
        /// <param name="chunkSize">The size of each read operation, defaults to 8192</param>
        /// <returns>TBD</returns>
        public static Source<ByteString, Task<IOResult>> FromInputStream(Func<Stream> createInputStream, int chunkSize = 8192)
        {
            var shape = new SourceShape<ByteString>(new Outlet<ByteString>("InputStreamSource"));
            var streamSource = new InputStreamSource(createInputStream, chunkSize, DefaultAttributes.InputStreamSource,
                shape);
            return new Source<ByteString, Task<IOResult>>(streamSource);
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> which when materialized will return an <see cref="Stream"/> which it is possible
        /// to write the ByteStrings to the stream this Source is attached to.
        /// 
        /// This Source is intended for inter-operation with legacy APIs since it is inherently blocking.
        /// 
        /// You can configure the default dispatcher for this Source by changing the "akka.stream.blocking-io-dispatcher" or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>.
        /// 
        /// The created <see cref="Stream"/> will be closed when the <see cref="Source{TOut,TMat}"/> is cancelled, and closing the <see cref="Stream"/>
        /// will complete this <see cref="Source{TOut,TMat}"/>.
        /// </summary>
        /// <param name="writeTimeout">The max time the write operation on the materialized OutputStream should block, defaults to 5 seconds</param>
        /// <returns>TBD</returns>
        public static Source<ByteString, Stream> AsOutputStream(TimeSpan? writeTimeout = null)
            => Source.FromGraph(new OutputStreamSourceStage(writeTimeout ?? TimeSpan.FromSeconds(5)));

        /// <summary>
        /// Creates a Sink which writes incoming <see cref="ByteString"/>s to an <see cref="Stream"/> created by the given function.
        /// 
        /// Materializes a <see cref="Task{TResult}"/> of <see cref="IOResult"/> that will be completed with the size of the file (in bytes) at the streams completion,
        /// and a possible exception if IO operation was not completed successfully.
        /// 
        /// You can configure the default dispatcher for this Source by changing the "akka.stream.blocking-io-dispatcher" or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>. 
        /// If <paramref name="autoFlush"/> is true the OutputStream will be flushed whenever a byte array is written, defaults to false.
        /// 
        /// The <see cref="Stream"/> will be closed when the stream flowing into this <see cref="Sink{TIn,TMat}"/> is completed. The <see cref="Sink{TIn,TMat}"/>
        /// will cancel the stream when the <see cref="Stream"/> is no longer writable.
        /// </summary>
        /// <param name="createOutputStream">A function which creates the <see cref="Stream"/> to write to</param>
        /// <param name="autoFlush">If set to true the <see cref="Stream"/> will be flushed whenever a byte array is written, default is false</param>
        /// <returns>TBD</returns>
        public static Sink<ByteString, Task<IOResult>> FromOutputStream(Func<Stream> createOutputStream, bool autoFlush = false)
        {
            var shape = new SinkShape<ByteString>(new Inlet<ByteString>("OutputStreamSink"));
            var streamSink = new OutputStreamSink(createOutputStream, DefaultAttributes.OutputStreamSink, shape,
                autoFlush);
            return new Sink<ByteString, Task<IOResult>>(streamSink);
        }

        /// <summary>
        /// Creates a Sink which when materialized will return an <see cref="Stream"/> which it is possible
        /// to read the values produced by the stream this Sink is attached to.
        /// 
        /// This Sink is intended for inter-operation with legacy APIs since it is inherently blocking.
        /// 
        /// You can configure the default dispatcher for this Source by changing the "akka.stream.blocking-io-dispatcher" or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>.
        /// 
        /// The <see cref="Stream"/> will be closed when the stream flowing into this <see cref="Sink{TIn,TMat}"/> completes, and
        /// closing the <see cref="Stream"/> will cancel this <see cref="Sink{TIn,TMat}"/>.
        /// </summary>
        /// <param name="readTimeout">The max time the read operation on the materialized stream should block</param>
        /// <returns>TBD</returns>
        public static Sink<ByteString, Stream> AsInputStream(TimeSpan? readTimeout = null)
        {
            readTimeout = readTimeout ?? TimeSpan.FromSeconds(5);
            return Sink.FromGraph(new InputStreamSinkStage(readTimeout.Value));
        }
    }
}
