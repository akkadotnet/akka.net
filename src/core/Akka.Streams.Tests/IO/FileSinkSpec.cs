//-----------------------------------------------------------------------
// <copyright file="FileSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.IO;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public class FileSinkSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;
        private readonly List<string> _testLines = new List<string>();
        private readonly List<ByteString> _testByteStrings;

        public FileSinkSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);

            foreach (var character in new[] { "a", "b", "c", "d", "e", "f" })
            {
                var line = "";
                for (var i = 0; i < 1000; i++)
                    line += character;
                // don't use Environment.NewLine - it can contain more than one byte length marker, 
                // causing tests to fail due to incorrect number of bytes in input string
                line += "\n";
                _testLines.Add(line);
            }

            _testByteStrings = _testLines.Select(ByteString.FromString).ToList();
        }

        [Fact]
        public void SynchronousFileSink_should_write_lines_to_a_file()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    var completion = Source.From(_testByteStrings).RunWith(FileIO.ToFile(f), _materializer);

                    completion.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    var result = completion.Result;
                    result.Count.Should().Be(6006);
                    CheckFileContent(f, _testLines.Aggregate((s, s1) => s + s1));
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_create_new_file_if_not_exists()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    var completion = Source.From(_testByteStrings).RunWith(FileIO.ToFile(f), _materializer);
                    completion.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    var result = completion.Result;
                    result.Count.Should().Be(6006);
                    CheckFileContent(f, _testLines.Aggregate((s, s1) => s + s1));
                }, false);
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_write_into_existing_file_without_wiping_existing_data()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(IEnumerable<string> lines) => Source.From(lines)
                        .Select(ByteString.FromString)
                        .RunWith(FileIO.ToFile(f, FileMode.OpenOrCreate), _materializer);

                    var completion1 = Write(_testLines);
                    completion1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                    var lastWrite = new string[100];
                    for (var i = 0; i < 100; i++)
                        lastWrite[i] = "x";

                    var completion2 = Write(lastWrite);
                    completion2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    var result = completion2.Result;

                    var lastWriteString = new string(lastWrite.SelectMany(x => x).ToArray());
                    result.Count.Should().Be(lastWriteString.Length);
                    var testLinesString = new string(_testLines.SelectMany(x => x).ToArray());
                    CheckFileContent(f, lastWriteString + testLinesString.Substring(100));
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_by_default_replace_the_existing_file()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(List<string> lines) =>
                        Source.From(lines).Select(ByteString.FromString)
                            .RunWith(FileIO.ToFile(f), _materializer);

                    Write(_testLines).AwaitResult();

                    var lastWrite = Enumerable.Range(0, 100).Select(_ => "x").ToList();
                    var result = Write(lastWrite).AwaitResult();

                    result.Count.Should().Be(lastWrite.Count);
                    CheckFileContent(f, string.Join("", lastWrite));
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_allow_appending_to_file()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(List<string> lines) => Source.From(lines)
                        .Select(ByteString.FromString)
                        .RunWith(FileIO.ToFile(f, fileMode: FileMode.Append), _materializer);

                    var completion1 = Write(_testLines);
                    completion1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    var result1 = completion1.Result;

                    var lastWrite = new List<string>();
                    for (var i = 0; i < 100; i++)
                        lastWrite.Add("x");

                    var completion2 = Write(lastWrite);
                    completion2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    var result2 = completion2.Result;

                    var lastWriteString = new string(lastWrite.SelectMany(x => x).ToArray());
                    var testLinesString = new string(_testLines.SelectMany(x => x).ToArray());

                    f.Length.Should().Be(result1.Count + result2.Count);

                    //NOTE: no new line at the end of the file - does JVM/linux appends new line at the end of the file in append mode?
                    CheckFileContent(f, testLinesString + lastWriteString);
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_allow_writing_from_specific_position_to_the_file()
        {
            this.AssertAllStagesStopped(() => 
            {
                TargetFile(f => 
                {
                    var testLinesCommon = new List<string>
                    {
                        new string('a', 1000) + "\n",
                        new string('b', 1000) + "\n",
                        new string('c', 1000) + "\n",
                        new string('d', 1000) + "\n",
                    };

                    var commonByteString = ByteString.FromString(testLinesCommon.Join("")).Compact();
                    var startPosition = commonByteString.Count;

                    var testLinesPart2 = new List<string>()
                    {
                        new string('x', 1000) + "\n",
                        new string('x', 1000) + "\n",
                    };

                    Task<IOResult> Write(List<string> lines, long pos) => Source.From(lines)
                        .Select(ByteString.FromString)
                        .RunWith(FileIO.ToFile(f, fileMode: FileMode.OpenOrCreate, startPosition: pos), _materializer);

                    var completion1 = Write(_testLines, 0);
                    var result1 = completion1.AwaitResult();

                    var completion2 = Write(testLinesPart2, startPosition);
                    var result2 = completion2.AwaitResult();

                    f.Length.ShouldBe(startPosition + result2.Count);
                    CheckFileContent(f, testLinesCommon.Join("") + testLinesPart2.Join(""));
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_use_dedicated_blocking_io_dispatcher_by_default()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
                    var materializer = ActorMaterializer.Create(sys);

                    try
                    {
                        //hack for Iterator.continually
                        Source.FromEnumerator(() => Enumerable.Repeat(_testByteStrings.Head(), Int32.MaxValue).GetEnumerator())
                            .RunWith(FileIO.ToFile(f), materializer);

                        ((ActorMaterializerImpl)materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);
                        var refs = ExpectMsg<StreamSupervisor.Children>().Refs;
                        //NOTE: Akka uses "fileSource" as name for DefaultAttributes.FileSink - I think it's mistake on the JVM implementation side
                        var actorRef = refs.First(@ref => @ref.Path.ToString().Contains("fileSink"));
                        Utils.AssertDispatcher(actorRef, "akka.stream.default-blocking-io-dispatcher");
                    }
                    finally
                    {
                        Shutdown(sys);
                    }
                });
            }, _materializer);
        }

        // FIXME: overriding dispatcher should be made available with dispatcher alias support in materializer (#17929)
        [Fact(Skip = "overriding dispatcher should be made available with dispatcher alias support in materializer")]
        public void SynchronousFileSink_should_allow_overriding_the_dispatcher_using_Attributes()
        {
            this.AssertAllStagesStopped(() =>
            {
                TargetFile(f =>
                {
                    var sys = ActorSystem.Create("dispatcher_testing", Utils.UnboundedMailboxConfig);
                    var materializer = ActorMaterializer.Create(sys);

                    try
                    {
                        //hack for Iterator.continually
                        Source.FromEnumerator(() => Enumerable.Repeat(_testByteStrings.Head(), Int32.MaxValue).GetEnumerator())
                            .To(FileIO.ToFile(f))
                            .WithAttributes(ActorAttributes.CreateDispatcher("akka.actor.default-dispatcher"));
                        //.Run(materializer);

                        ((ActorMaterializerImpl)materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);
                        var actorRef = ExpectMsg<StreamSupervisor.Children>().Refs.First(@ref => @ref.Path.ToString().Contains("File"));
                        Utils.AssertDispatcher(actorRef, "akka.actor.default-dispatcher");
                    }
                    finally
                    {
                        Shutdown(sys);
                    }
                });
            }, _materializer);
        }

        [Fact]
        public void SynchronousFileSink_should_write_single_line_to_a_file_from_lazy_sink()
        {
            this.AssertAllStagesStopped(() => 
            {
                TargetFile(f => 
                {
                    var lazySink = Sink.LazySink(
                        (ByteString _) => Task.FromResult(FileIO.ToFile(f)),
                            () => Task.FromResult(IOResult.Success(0)))
                            .MapMaterializedValue(t => t.AwaitResult());

                    var completion = Source.From(new []{_testByteStrings.Head()})
                        .RunWith(lazySink, _materializer);

                    completion.AwaitResult();
                    CheckFileContent(f, _testLines.Head());
                });
            }, _materializer);
        }

        private static void TargetFile(Action<FileInfo> block, bool create = true)
        {
            var targetFile = new FileInfo(Path.Combine(Path.GetTempPath(), "synchronous-file-sink.tmp"));

            if (!create)
                targetFile.Delete();
            else
                targetFile.Create().Dispose();

            try
            {
                block(targetFile);
            }
            finally
            {
                //give the system enough time to shutdown and release the file handle
                Thread.Sleep(500);
                targetFile.Delete();
            }
        }

        private static void CheckFileContent(FileInfo f, string contents)
        {
            var s = f.OpenText();
            var cont = s.ReadToEnd();
            s.Dispose();
            cont.Should().Be(contents);
        }
    }
}
