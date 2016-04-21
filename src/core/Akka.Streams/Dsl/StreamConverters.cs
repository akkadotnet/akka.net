//-----------------------------------------------------------------------
// <copyright file="StreamConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /**
   * Creates a Source from an [[InputStream]] created by the given function.
   * Emitted elements are `chunkSize` sized [[akka.util.ByteString]] elements,
   * except the final element, which will be up to `chunkSize` in size.
   *
   * You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
   * set it for a given Source by using [[ActorAttributes]].
   *
   * It materializes a [[Future]] of [[IOResult]] containing the number of bytes read from the source file upon completion,
   * and a possible exception if IO operation was not completed successfully.
   *
   * The created [[InputStream]] will be closed when the [[Source]] is cancelled.
   *
   * @param in a function which creates the InputStream to read from
   * @param chunkSize the size of each read operation, defaults to 8192
   */

        public static Source<ByteString, Task<IOResult>> FromInputStream(Func<Stream> createInputStream, int chunkSize = 8192) =>
                new Source<ByteString, Task<IOResult>>(new InputStreamSource(createInputStream, chunkSize,
                    DefaultAttributes.InputStreamSource,
                    new SourceShape<ByteString>(new Outlet<ByteString>("InputStreamSource"))));

        /**
 * Creates a Source which when materialized will return an [[OutputStream]] which it is possible
 * to write the ByteStrings to the stream this Source is attached to.
 *
 * This Source is intended for inter-operation with legacy APIs since it is inherently blocking.
 *
 * You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
 * set it for a given Source by using [[ActorAttributes]].
 *
 * The created [[OutputStream]] will be closed when the [[Source]] is cancelled, and closing the [[OutputStream]]
 * will complete this [[Source]].
 *
 * @param writeTimeout the max time the write operation on the materialized OutputStream should block, defaults to 5 seconds
 */

        public static Source<ByteString, Stream> AsOutputStream(TimeSpan? writeTimeout = null)
            => Source.FromGraph(new OutputStreamSourceStage(writeTimeout ?? TimeSpan.FromSeconds(5)));


        /**
   * Creates a Sink which writes incoming [[ByteString]]s to an [[OutputStream]] created by the given function.
   *
   * Materializes a [[Future]] of [[IOResult]] that will be completed with the size of the file (in bytes) at the streams completion,
   * and a possible exception if IO operation was not completed successfully.
   *
   * You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
   * set it for a given Source by using [[ActorAttributes]].
   * If `autoFlush` is true the OutputStream will be flushed whenever a byte array is written, defaults to false.
   *
   * The [[OutputStream]] will be closed when the stream flowing into this [[Sink]] is completed. The [[Sink]]
   * will cancel the stream when the [[OutputStream]] is no longer writable.
   */

        public static Sink<ByteString, Task<IOResult>> FromOutputStream(Func<Stream> createOutputStream, bool autoFlush = false) =>
                new Sink<ByteString, Task<IOResult>>(new OutputStreamSink(createOutputStream,
                    DefaultAttributes.OutputStreamSink,
                    new SinkShape<ByteString>(new Inlet<ByteString>("OutputStreamSink")), autoFlush));

        /**
 * Creates a Sink which when materialized will return an [[InputStream]] which it is possible
 * to read the values produced by the stream this Sink is attached to.
 *
 * This Sink is intended for inter-operation with legacy APIs since it is inherently blocking.
 *
 * You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
 * set it for a given Source by using [[ActorAttributes]].
 *
 * The [[InputStream]] will be closed when the stream flowing into this [[Sink]] completes, and
 * closing the [[InputStream]] will cancel this [[Sink]].
 *
 * @param readTimeout the max time the read operation on the materialized InputStream should block
 */

        public static Sink<ByteString, Stream> AsInputStream(TimeSpan? readTimeout = null)
        {
            readTimeout = readTimeout ?? TimeSpan.FromSeconds(5);
            return Sink.FromGraph(new InputStreamSinkStage(readTimeout.Value));
        }
    }
}