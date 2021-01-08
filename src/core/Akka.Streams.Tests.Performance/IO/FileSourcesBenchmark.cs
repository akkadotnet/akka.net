//-----------------------------------------------------------------------
// <copyright file="FileSourcesBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.IO;
using NBench;

namespace Akka.Streams.Tests.Performance.IO
{
    public class FileSourcesBenchmark
    {
        private const int BufferSize = 2048;
        private ActorSystem _actorSystem;
        private ActorMaterializer _materializer;
        private FileInfo _file;
        private Source<ByteString, Task<IOResult>> _fileChannelSource;
        private Source<ByteString, Task<IOResult>> _fileInputStreamSource;
        private Source<ByteString, NotUsed> _ioSourceLinesIterator;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _actorSystem = ActorSystem.Create("FileSourcesBenchmark");
            _materializer = _actorSystem.Materializer();

            _file = CreateFile();

            _fileChannelSource = FileIO.FromFile(_file, BufferSize);
            _fileInputStreamSource = StreamConverters.FromInputStream(() => File.OpenRead(_file.FullName), BufferSize);
            _ioSourceLinesIterator = Source.FromEnumerator(() => File.ReadLines(_file.FullName).Select(ByteString.FromString).GetEnumerator());
        }

        private FileInfo CreateFile()
        {
            var line = Enumerable.Repeat("x", 2048).Aggregate("", (agg, s) => agg + s) + "\n";

            var file = new FileInfo(Path.GetTempFileName());
            File.WriteAllLines(file.FullName, Enumerable.Repeat(line, 10 * 39062));
            return file;
        }

        [PerfCleanup]
        public void Shutdown()
        {
            _actorSystem.Terminate().Wait(TimeSpan.FromSeconds(5));
            try
            {
                _file.Delete();
            }
            catch 
            {
                //try again...
                Thread.Sleep(TimeSpan.FromSeconds(10));
                _file.Delete();
            }
        }


        [PerfBenchmark(Description = "Test the performance of a FileSource using a file channel",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 1)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2500)]
        public void FileChannel()
            => _fileChannelSource.To(Sink.Ignore<ByteString>())
                .Run(_materializer)
                .Wait(TimeSpan.FromMinutes(1));


        [PerfBenchmark(Description = "Test the performance of a FileSource using a file channel without read ahead",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 1)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2000)]
        public void FileChannel_without_read_ahead()
            => _fileChannelSource.WithAttributes(Attributes.CreateInputBuffer(1, 1))
                .To(Sink.Ignore<ByteString>())
                .Run(_materializer)
                .Wait(TimeSpan.FromMinutes(1));


        [PerfBenchmark(Description = "Test the performance of a FileSource using a file stream",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 1)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2000)]
        public void FileStream()
            => _fileInputStreamSource.To(Sink.Ignore<ByteString>())
                .Run(_materializer)
                .Wait(TimeSpan.FromMinutes(1));


        [PerfBenchmark(Description = "Test the performance of a FileSource using File.ReadLines enumerator",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 1)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 8000)]
        public void Naive_IO_lines_enumerator()
        {
            var c = new TaskCompletionSource<int>();

            _ioSourceLinesIterator.To(Sink.OnComplete<ByteString>(() => c.SetResult(-1), _ => { })).Run(_materializer);

            c.Task.Wait(TimeSpan.FromMinutes(1));
        }
    }
}
