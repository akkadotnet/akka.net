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
        /// this materialized graph. It can then be used to consume events incoming from the graph. It will
        /// also be completed once upstream completes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bufferSize"></param>
        /// <param name="singleReader"></param>
        /// <param name="fullMode"></param>
        /// <returns></returns>
        public static Sink<T, ChannelReader<T>> AsReader<T>(int bufferSize, bool singleReader = false, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait) =>
            Sink.FromGraph(new ChannelReaderSink<T>(bufferSize, singleReader, fullMode));
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNullException(string name) =>
            throw new ArgumentNullException(name, "ChannelSink.FromWriter received null instead of ChannelWriter`1.");
    }
}
