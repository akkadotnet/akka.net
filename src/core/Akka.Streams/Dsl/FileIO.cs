//-----------------------------------------------------------------------
// <copyright file="FileIO.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Implementation.IO;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;

namespace Akka.Streams.Dsl
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// TBD
    /// </summary>
    public static class FileIO
    {
        /// <summary>
        /// Creates a Source from a Files contents.
        /// Emitted elements are <paramref name="chunkSize"/> sized <see cref="ByteString"/> elements,
        /// except the final element, which will be up to <paramref name="chunkSize"/> in size.
        ///
        /// You can configure the default dispatcher for this Source by changing the "akka.stream.blocking-io-dispatcher" or
        /// set it for a given Source by using <see cref="ActorAttributes"/>.
        ///
        /// It materializes a <see cref="Task{TResult}"/> of <see cref="IOResult"/> containing the number of bytes read from the source file upon completion,
        /// and a possible exception if IO operation was not completed successfully.
        /// </summary>
        /// <param name="f">the File to read from</param>
        /// <param name="chunkSize">the size of each read operation, defaults to 8192</param>
        /// <param name="startPosition">the start position to read from, defaults to 0</param>
        /// <returns>TBD</returns>
        public static Source<ByteString, Task<IOResult>> FromFile(FileInfo f, int chunkSize = 8192, long startPosition = 0) =>
            new Source<ByteString, Task<IOResult>>(new FileSource(f, chunkSize, startPosition, DefaultAttributes.FileSource,
                new SourceShape<ByteString>(new Outlet<ByteString>("FileSource"))));

        /// <summary>
        /// Creates a Sink which writes incoming <see cref="ByteString"/> elements to the given file. Overwrites existing files
        /// by truncating their contents as default.
        ///
        /// Materializes a <see cref="Task{TResult}"/> of <see cref="IOResult"/> that will be completed with the size of the file(in bytes) at the streams completion,
        /// and a possible exception if IO operation was not completed successfully.
        ///
        /// This source is backed by an Actor which will use the dedicated "akka.stream.blocking-io-dispatcher",
        /// unless configured otherwise by using <see cref="ActorAttributes"/>.
        /// </summary>
        /// <param name="f">the file to write to</param>
        /// <param name="fileMode">the write file mode, defaults to <see cref="FileMode.OpenOrCreate"/></param>
        /// <param name="startPosition">the start position to write to, defaults to 0</param>
        /// <returns>TBD</returns>
        public static Sink<ByteString, Task<IOResult>> ToFile(FileInfo f, FileMode? fileMode = null, long startPosition = 0) =>
            new Sink<ByteString, Task<IOResult>>(new FileSink(f, startPosition, fileMode ?? FileMode.Create, DefaultAttributes.FileSink,
                new SinkShape<ByteString>(new Inlet<ByteString>("FileSink"))));
    }
}
