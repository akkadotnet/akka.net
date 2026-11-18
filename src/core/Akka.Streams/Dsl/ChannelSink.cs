//-----------------------------------------------------------------------
// <copyright file="ChannelSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Provides sinks that write stream elements to <see cref="ChannelWriter{T}"/> instances
    /// or expose a bounded channel through a <see cref="ChannelReader{T}"/>.
    /// </summary>
    public static class ChannelSink
    {
        /// <summary>
        /// Creates a Sink that will emit incoming events directly into the provided <see cref="ChannelWriter{T}"/>.
        /// It will handle backpressure automatically by respecting the channel's capacity.
        /// </summary>
        /// <typeparam name="T">Type of events passed to <paramref name="writer"/>.</typeparam>
        /// <param name="writer">A <see cref="ChannelWriter{T}"/> to pass events emitted from the materialized graph to.</param>
        /// <param name="isOwner">
        /// Determines whether the materialized graph should take ownership of the <paramref name="writer"/>.
        /// When <c>true</c>, the sink will call <c>Complete()</c> when the stream completes normally,
        /// and <c>TryComplete(Exception)</c> if the stream fails.
        /// When <c>false</c>, the sink will not complete the writer, allowing it to be used by multiple producers
        /// or managed externally.
        /// </param>
        /// <returns>A <see cref="Sink{TIn,TMat}"/> that writes to the provided channel.</returns>
        public static Sink<T, NotUsed> FromWriter<T>(ChannelWriter<T> writer, bool isOwner)
        {
            if (writer is null)
                ThrowArgumentNullException("writer");

            return Sink.FromGraph(new ChannelWriterSink<T>(writer, isOwner));
        }

        /// <summary>
        /// Creates a sink that upon materialization, returns a <see cref="ChannelReader{T}"/> connected with
        /// this materialized graph. The reader can be used to consume events emitted by the graph. The channel
        /// closes after normal upstream completion, once any pending write has finished. If upstream fails, the
        /// channel closes with that failure. Buffered elements remain readable; the reader's <see cref="ChannelReader{T}.Completion"/>
        /// task completes or faults after no more data is available to read.
        /// When the channel is full, <see cref="BoundedChannelFullMode.Wait"/> waits for space and backpressures
        /// upstream. The drop modes can discard an incoming or buffered element according to the selected mode.
        /// </summary>
        /// <typeparam name="T">The type of elements written to the channel.</typeparam>
        /// <param name="bufferSize">The maximum number of elements the bounded channel can store.</param>
        /// <param name="singleReader">Indicates that the caller guarantees at most one read operation at a time.</param>
        /// <param name="fullMode">The behavior to use when the bounded channel is full.</param>
        /// <returns>A sink that materializes to a reader for its bounded channel.</returns>
        public static Sink<T, ChannelReader<T>> AsReader<T>(int bufferSize, bool singleReader = false, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait) =>
            Sink.FromGraph(new ChannelReaderSink<T>(bufferSize, singleReader, fullMode));
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNullException(string name) =>
            throw new ArgumentNullException(name, "ChannelSink.FromWriter received null instead of ChannelWriter`1.");
    }
}
