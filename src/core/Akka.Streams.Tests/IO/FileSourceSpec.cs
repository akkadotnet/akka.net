//-----------------------------------------------------------------------
// <copyright file="FileSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public class FileSourceSpec : AkkaSpec
    {
        private readonly string _testText;
        private readonly ActorMaterializer _materializer;
        private readonly FileInfo _testFilePath = new FileInfo(Path.Combine(Path.GetTempPath(), "file-source-spec.tmp"));
        private FileInfo _manyLinesPath;

        public FileSourceSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);

            var sb = new StringBuilder(6000);
            foreach (var character in new[] { "a", "b", "c", "d", "e", "f" })
                for (var i = 0; i < 1000; i++)
                    sb.Append(character);

            _testText = sb.ToString();
        }

        [Fact]
        public void FileSource_should_read_contents_from_a_file()
        {
            this.AssertAllStagesStopped(() =>
            {
                var chunkSize = 512;
                var bufferAttributes = Attributes.CreateInputBuffer(1, 2);

                var p = FileIO.FromFile(TestFile(), chunkSize)
                    .WithAttributes(bufferAttributes)
                    .RunWith(Sink.AsPublisher<ByteString>(false), _materializer);

                var c = this.CreateManualSubscriberProbe<ByteString>();
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                var remaining = _testText;
                var nextChunk = new Func<string>(() =>
                {
                    string chunks;

                    if (remaining.Length <= chunkSize)
                    {
                        chunks = remaining;
                        remaining = string.Empty;
                    }
                    else
                    {
                        chunks = remaining.Substring(0, chunkSize);
                        remaining = remaining.Substring(chunkSize);
                    }

                    return chunks;
                });

                sub.Request(1);
                c.ExpectNext().ToString(Encoding.UTF8).Should().Be(nextChunk());
                sub.Request(1);
                c.ExpectNext().ToString(Encoding.UTF8).Should().Be(nextChunk());
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

                sub.Request(200);
                var expectedChunk = nextChunk();
                while (!string.IsNullOrEmpty(expectedChunk))
                {
                    var actual = c.ExpectNext().ToString(Encoding.UTF8);
                    actual.Should().Be(expectedChunk);
                    expectedChunk = nextChunk();
                }
                sub.Request(1);
                c.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void Filesource_could_read_partial_contents_from_a_file()
        {
            this.AssertAllStagesStopped(() => 
            {
                var chunkSize = 512;
                var startPosition = 1000;
                var bufferAttributes = Attributes.CreateInputBuffer(1, 2);

                var p = FileIO.FromFile(TestFile(), chunkSize, startPosition)
                    .WithAttributes(bufferAttributes)
                    .RunWith(Sink.AsPublisher<ByteString>(false), _materializer);

                var c = this.CreateManualSubscriberProbe<ByteString>();
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                var remaining = _testText.Substring(1000);

                var nextChunk = new Func<string>(() => {
                    string chunks;

                    if (remaining.Length <= chunkSize)
                    {
                        chunks = remaining;
                        remaining = string.Empty;
                    }
                    else
                    {
                        chunks = remaining.Substring(0, chunkSize);
                        remaining = remaining.Substring(chunkSize);
                    }

                    return chunks;
                });

                sub.Request(5000);

                var expectedChunk = nextChunk();
                for(int i=0; i<10; ++i)
                {
                    c.ExpectNext().ToString().Should().Be(expectedChunk);
                    expectedChunk = nextChunk();
                }
                c.ExpectComplete();

            }, _materializer);
        }

        [Fact]
        public void FileSource_should_complete_only_when_all_contents_of_a_file_have_been_signalled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var chunkSize = 512;
                var bufferAttributes = Attributes.CreateInputBuffer(1, 2);
                var demandAllButOnechunks = _testText.Length / chunkSize - 1;

                var p = FileIO.FromFile(TestFile(), chunkSize)
                    .WithAttributes(bufferAttributes)
                    .RunWith(Sink.AsPublisher<ByteString>(false), _materializer);

                var c = this.CreateManualSubscriberProbe<ByteString>();
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                var remaining = _testText;
                var nextChunk = new Func<string>(() =>
                {
                    string chunks;

                    if (remaining.Length <= chunkSize)
                    {
                        chunks = remaining;
                        remaining = string.Empty;
                    }
                    else
                    {
                        chunks = remaining.Substring(0, chunkSize);
                        remaining = remaining.Substring(chunkSize);
                    }

                    return chunks;
                });

                sub.Request(demandAllButOnechunks);
                for (var i = 0; i < demandAllButOnechunks; i++)
                    c.ExpectNext().ToString(Encoding.UTF8).Should().Be(nextChunk());
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

                sub.Request(1);
                c.ExpectNext().ToString(Encoding.UTF8).Should().Be(nextChunk());
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

                sub.Request(1);
                c.ExpectNext().ToString(Encoding.UTF8).Should().Be(nextChunk());
                c.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void FileSource_should_open_file_in_shared_mode_for_reading_multiple_times()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testFile = TestFile();
                var p1 = FileIO.FromFile(testFile).RunWith(Sink.AsPublisher<ByteString>(false), _materializer);
                var p2 = FileIO.FromFile(testFile).RunWith(Sink.AsPublisher<ByteString>(false), _materializer);
                
                var c1 = this.CreateManualSubscriberProbe<ByteString>();
                var c2 = this.CreateManualSubscriberProbe<ByteString>();
                p1.Subscribe(c1);
                p2.Subscribe(c2);
                var s1 = c1.ExpectSubscription();
                var s2 = c2.ExpectSubscription();

                s1.Request(5000);
                s2.Request(5000);

                c1.ExpectNext();
                c2.ExpectNext();

            }, _materializer);
        }

        [Fact]
        public void FileSource_should_onError_with_failure_and_return_a_failed_IOResult_when_trying_to_read_from_file_which_does_not_exist()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = FileIO.FromFile(NotExistingFile())
                    .ToMaterialized(Sink.AsPublisher<ByteString>(false), Keep.Both)
                    .Run(_materializer);
                var r = t.Item1;
                var p = t.Item2;

                var c = this.CreateManualSubscriberProbe<ByteString>();
                p.Subscribe(c);

                c.ExpectSubscription();
                c.ExpectError();
                r.AwaitResult(Dilated(TimeSpan.FromSeconds(3))).WasSuccessful.ShouldBeFalse();
            }, _materializer);
        }

        [Theory]
        [InlineData(512, 2)]
        [InlineData(512, 4)]
        [InlineData(2048, 2)]
        [InlineData(2048, 4)]
        public void FileSource_should_count_lines_in_a_real_file(int chunkSize, int readAhead)
        {
            var s = FileIO.FromFile(ManyLines(), chunkSize)
                .WithAttributes(Attributes.CreateInputBuffer(readAhead, readAhead));
            var f = s.RunWith(
                Sink.Aggregate<ByteString, int>(0, (acc, l) => acc + l.ToString(Encoding.UTF8).Count(c => c == '\n')),
                _materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var lineCount = f.Result;
            lineCount.Should().Be(LinesCount);
        }

        [Fact]
        public void FileSource_should_use_dedicated_blocking_io_dispatcher_by_default()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
                var materializer = sys.Materializer();

                try
                {
                    var p = FileIO.FromFile(ManyLines()).RunWith(this.SinkProbe<ByteString>(), materializer);
                    (materializer as ActorMaterializerImpl).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);

                    var actorRef = ExpectMsg<StreamSupervisor.Children>().Refs.First(r => r.Path.ToString().Contains("fileSource"));
                    try
                    {
                        Utils.AssertDispatcher(actorRef, "akka.stream.default-blocking-io-dispatcher");
                    }
                    finally
                    {
                        p.Cancel();
                    }
                }
                finally
                {
                    Shutdown(sys);
                }
            }, _materializer);
        }

        [Fact(Skip = "overriding dispatcher should be made available with dispatcher alias support in materializer (#17929)")]
        //FIXME: overriding dispatcher should be made available with dispatcher alias support in materializer (#17929)
        public void FileSource_should_should_allow_overriding_the_dispatcher_using_Attributes()
        {
            var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
            var materializer = sys.Materializer();

            try
            {
                var p = FileIO.FromFile(ManyLines())
                    .WithAttributes(ActorAttributes.CreateDispatcher("akka.actor.default-dispatcher"))
                    .RunWith(this.SinkProbe<ByteString>(), materializer);
                (materializer as ActorMaterializerImpl).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);

                var actorRef = ExpectMsg<StreamSupervisor.Children>().Refs.First(r => r.Path.ToString().Contains("File"));
                try
                {
                    Utils.AssertDispatcher(actorRef, "akka.actor.default-dispatcher");
                }
                finally
                {
                    p.Cancel();
                }
            }
            finally
            {
                Shutdown(sys);
            }
        }

        [Fact]
        public void FileSource_should_not_signal_OnComplete_more_than_once()
        {
            FileIO.FromFile(TestFile(), 2*_testText.Length)
                .RunWith(this.SinkProbe<ByteString>(), _materializer)
                .RequestNext(ByteString.FromString(_testText))
                .ExpectComplete()
                .ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        private int LinesCount { get; } = 2000 + new Random().Next(300);

        private FileInfo ManyLines()
        {
            _manyLinesPath = new FileInfo(Path.Combine(Path.GetTempPath(), $"file-source-spec-lines_{LinesCount}.tmp"));
            var line = "";
            var lines = new List<string>();
            for (var i = 0; i < LinesCount; i++)
                line += "a";
            for (var i = 0; i < LinesCount; i++)
                lines.Add(line);

            File.AppendAllLines(_manyLinesPath.FullName, lines);
            return _manyLinesPath;
        }

        private FileInfo TestFile()
        {
            File.AppendAllText(_testFilePath.FullName, _testText);
            return _testFilePath;
        }

        private FileInfo NotExistingFile()
        {
            var f = new FileInfo(Path.Combine(Path.GetTempPath(), "not-existing-file.tmp"));
            if (f.Exists)
                f.Delete();
            return f;
        }

        protected override void AfterAll()
        {
            base.AfterAll();

            //give the system enough time to shutdown and release the file handle
            Thread.Sleep(500);
            _manyLinesPath?.Delete();
            _testFilePath?.Delete();
        }
    }
}
