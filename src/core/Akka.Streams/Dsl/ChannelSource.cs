//-----------------------------------------------------------------------
// <copyright file="ChannelSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Container class for Akka.Streams <see cref="Source{TOut,TMat}"/> factory methods,
    /// which can be used to create sources from readable channels.
    /// </summary>
    public static class ChannelSource
    {
        /// <summary>
        /// Creates an Akka.Streams <see cref="Source{TOut,TMat}"/> from a given instance of
        /// <see cref="ChannelReader{T}"/>. It will propagate backpressure from the downstream
        /// to guarantee resource-safe communication as well as will react when <paramref name="reader"/>
        /// will complete (successfully or with failure) and finish downstream accordingly.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static Source<T, NotUsed> FromReader<T>(ChannelReader<T> reader)
        {
            if (reader is null)
                ThrowArgumentNullException("reader");

            return Source.FromGraph(new ChannelReaderSource<T>(reader));
        }

        public static Source<T, ChannelWriter<T>> Create<T>(int bufferSize,
            bool singleWriter = false,
            BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        {
            return Source.FromGraph(
                new ChannelReaderWithMaterializedWriterSource<T>(bufferSize,
                    singleWriter, fullMode));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNullException(string name) => 
            throw new ArgumentNullException(name, "ChannelSource.FromReader expected ChannelReader`1 but received null.");
    }
}
