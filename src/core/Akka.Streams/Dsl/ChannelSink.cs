// //-----------------------------------------------------------------------
// // <copyright file="ChannelSink.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    public static class ChannelSink
    {
        /// <summary>
        /// Creates a Sink, that will emit incoming events directly into provider <see cref="ChannelWriter{T}"/>.
        /// It will handle problems such as backpressure and <paramref name="writer"/>. When <paramref name="isOwner"/>
        /// is set to <c>true</c>, it will also take responsibility to complete given <paramref name="writer"/>.
        /// </summary>
        /// <typeparam name="T">Type of events passed to <paramref name="writer"/>.</typeparam>
        /// <param name="writer">A <see cref="ChannelWriter{T}"/> to pass events emitted from materialized graph to.</param>
        /// <param name="isOwner">Determines materialized graph should be responsible for completing given <paramref name="writer"/>.</param>
        /// <returns></returns>
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
            Sink.FromGraph(new ChannelReaderSink<T>(bufferSize, singleReader));
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNullException(string name) =>
            throw new ArgumentNullException(name, "ChannelSink.FromWriter received null instead of ChannelWriter`1.");
    }
}