//-----------------------------------------------------------------------
// <copyright file="FileSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.IO;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Tests.Shared.Internals;
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
        private readonly TimeSpan _expectTimeout = TimeSpan.FromSeconds(10);

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
            Within(_expectTimeout, () => 
                {
                    TargetFile(f =>
                    {
                        var completion = Source.From(_testByteStrings)
                            .RunWith(FileIO.ToFile(f), _materializer);

                        completion.AwaitResult(Remaining);
                        var result = completion.Result;
                        result.Count.Should().Be(6006);

                        AwaitAssert(
                            () => CheckFileContent(f, _testLines.Aggregate((s, s1) => s + s1)),
                            Remaining);
                    }, _materializer);
                });
        }

        [Fact]
        public void SynchronousFileSink_should_create_new_file_if_not_exists()
        {
            Within(_expectTimeout, () =>
            {
                TargetFile(f =>
                {
                    var completion = Source.From(_testByteStrings)
                        .RunWith(FileIO.ToFile(f), _materializer);

                    completion.AwaitResult(Remaining);
                    var result = completion.Result;
                    result.Count.Should().Be(6006);
                    AwaitAssert(
                        () => CheckFileContent(f, _testLines.Aggregate((s, s1) => s + s1)),
                        Remaining);
                }, _materializer, false);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_write_into_existing_file_without_wiping_existing_data()
        {
            Within(_expectTimeout, () =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(IEnumerable<string> lines) => Source.From(lines)
                        .Select(ByteString.FromString)
                        .RunWith(FileIO.ToFile(f, FileMode.OpenOrCreate), _materializer);

                    var completion1 = Write(_testLines);
                    completion1.AwaitResult(Remaining);

                    var lastWrite = new string[100];
                    for (var i = 0; i < 100; i++)
                        lastWrite[i] = "x";

                    var completion2 = Write(lastWrite);
                    completion2.AwaitResult(Remaining);
                    var result = completion2.Result;

                    var lastWriteString = new string(lastWrite.SelectMany(x => x).ToArray());
                    result.Count.Should().Be(lastWriteString.Length);
                    var testLinesString = new string(_testLines.SelectMany(x => x).ToArray());

                    AwaitAssert(
                        () => CheckFileContent(f, lastWriteString + testLinesString.Substring(100)),
                        Remaining);
                }, _materializer);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_by_default_replace_the_existing_file()
        {
            Within(_expectTimeout, () =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(List<string> lines) =>
                        Source.From(lines).Select(ByteString.FromString)
                            .RunWith(FileIO.ToFile(f), _materializer);

                    var task1 = Write(_testLines);
                    task1.AwaitResult(Remaining);
                    var lastWrite = Enumerable.Range(0, 100).Select(_ => "x").ToList();

                    var task2 = Write(lastWrite);
                    var result = task2.AwaitResult(Remaining);

                    result.Count.Should().Be(lastWrite.Count);

                    AwaitAssert(
                        () => CheckFileContent(f, string.Join("", lastWrite)),
                        Remaining);
                }, _materializer);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_allow_appending_to_file()
        {
            Within(_expectTimeout, () =>
            {
                TargetFile(f =>
                {
                    Task<IOResult> Write(List<string> lines) => Source.From(lines)
                        .Select(ByteString.FromString)
                        .RunWith(FileIO.ToFile(f, fileMode: FileMode.Append), _materializer);

                    var completion1 = Write(_testLines);
                    completion1.AwaitResult(Remaining);
                    var result1 = completion1.Result;

                    var lastWrite = new List<string>();
                    for (var i = 0; i < 100; i++)
                        lastWrite.Add("x");

                    var completion2 = Write(lastWrite);
                    completion2.AwaitResult(Remaining);
                    var result2 = completion2.Result;

                    var lastWriteString = new string(lastWrite.SelectMany(x => x).ToArray());
                    var testLinesString = new string(_testLines.SelectMany(x => x).ToArray());

                    f.Length.Should().Be(result1.Count + result2.Count);

                    //NOTE: no new line at the end of the file - does JVM/linux appends new line at the end of the file in append mode?
                    AwaitAssert(
                        () => CheckFileContent(f, testLinesString + lastWriteString),
                        Remaining);
                }, _materializer);
            });

        }

        [Fact]
        public void SynchronousFileSink_should_allow_writing_from_specific_position_to_the_file()
        {
            Within(_expectTimeout, () =>
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
                        .RunWith(
                            FileIO.ToFile(f, fileMode: FileMode.OpenOrCreate, startPosition: pos),
                            _materializer);

                    var completion1 = Write(_testLines, 0);
                    completion1.AwaitResult(Remaining);

                    var completion2 = Write(testLinesPart2, startPosition);
                    var result2 = completion2.AwaitResult(Remaining);

                    f.Length.ShouldBe(startPosition + result2.Count);

                    AwaitAssert(
                        () => CheckFileContent(f, testLinesCommon.Join("") + testLinesPart2.Join("")),
                        Remaining);
                }, _materializer);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_use_dedicated_blocking_io_dispatcher_by_default()
        {
            Within(_expectTimeout, () =>
            {
                // This is technically incorrect, we're (ab)using TargetFile() just to provide
                // the necessary FileInfo, ignoring the fact that we're using a different
                // materializer, because we will shut down the system before we're exiting anyway.
                TargetFile(f =>
                {
                    var sys = ActorSystem.Create("FileSinkSpec-dispatcher-testing-1", Utils.UnboundedMailboxConfig);
                    var materializer = ActorMaterializer.Create(sys);
                    try
                    {
                        //hack for Iterator.continually
                        Source
                            .FromEnumerator(() => Enumerable.Repeat(_testByteStrings.Head(), int.MaxValue).GetEnumerator())
                            .RunWith(FileIO.ToFile(f), materializer);

                        ((ActorMaterializerImpl)materializer)
                            .Supervisor
                            .Tell(StreamSupervisor.GetChildren.Instance, TestActor);
                        var refs = ExpectMsg<StreamSupervisor.Children>().Refs;
                        var actorRef = refs.First(@ref => @ref.Path.ToString().Contains("fileSink"));

                        // haven't figured out why this returns the aliased id rather than the id, but the stage is going away so whatever
                        Utils.AssertDispatcher(actorRef, ActorAttributes.IODispatcher.Name);
                    }
                    finally
                    {
                        Shutdown(sys);
                    }
                }, _materializer);
            });
        }

        // FIXME: overriding dispatcher should be made available with dispatcher alias support in materializer (#17929)
        [Fact(Skip = "overriding dispatcher should be made available with dispatcher alias support in materializer")]
        public void SynchronousFileSink_should_allow_overriding_the_dispatcher_using_Attributes()
        {
            Within(_expectTimeout, () =>
            {
                // This is technically incorrect, we're (ab)using TargetFile() just to provide
                // the necessary FileInfo, ignoring the fact that we're using a different
                // materializer, because we will shut down the system before we're exiting anyway.
                TargetFile(f =>
                {
                    var sys = ActorSystem.Create("FileSinkSpec-dispatcher-testing-2", Utils.UnboundedMailboxConfig);
                    var materializer = ActorMaterializer.Create(sys);
                    try
                    {
                        //hack for Iterator.continually
                        Source.FromEnumerator(() => Enumerable.Repeat(_testByteStrings.Head(), int.MaxValue).GetEnumerator())
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
                }, _materializer);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_write_single_line_to_a_file_from_lazy_sink()
        {
            Within(_expectTimeout, () =>
            {
                TargetFile(f => 
                {
                    var lazySink = Sink.LazySink(
                            (ByteString _) => Task.FromResult(FileIO.ToFile(f)),
                            () => Task.FromResult(IOResult.Success(0)))
                        .MapMaterializedValue(t => t.AwaitResult());

                    var completion = Source.From(new []{_testByteStrings.Head()})
                        .RunWith(lazySink, _materializer);

                    completion.AwaitResult(Remaining);
                    AwaitAssert(
                        () => CheckFileContent(f, _testLines.Head()),
                        Remaining);
                }, _materializer);
            });
        }

        [Fact]
        public void SynchronousFileSink_should_write_each_element_if_auto_flush_is_set()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                TargetFile(f => 
                {
                    var (actor, task) = Source.ActorRef<string>(64, OverflowStrategy.DropNew)
                        .Select(ByteString.FromString)
                        .ToMaterialized(
                            FileIO.ToFile(f, fileMode: FileMode.OpenOrCreate, startPosition: 0, autoFlush:true), 
                            Keep.Both)
                        .Run(_materializer);
                    Watch(actor);

                    actor.Tell("a\n");
                    actor.Tell("b\n");

                    AwaitAssert(() =>
                    {
                        CheckFileContent(f, "a\nb\n");
                    }, Remaining);

                    actor.Tell("a\n");
                    actor.Tell("b\n");

                    actor.Tell(new Status.Success(NotUsed.Instance));

                    // We still have to wait for the task to complete, because the signal
                    // came from the FileSink actor, not the source actor.
                    task.AwaitResult(Remaining);
                    ExpectTerminated(actor, Remaining);

                    f.Length.ShouldBe(8);
                    CheckFileContent(f, "a\nb\na\nb\n");
                }, _materializer);
            });
        }

        private void TargetFile(
            Action<FileInfo> block, 
            ActorMaterializer materializer, 
            bool create = true)
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
                // this is the proverbial stream kill switch, make sure that all streams
                // are dead so that the file handle would be released
                this.AssertAllStagesStopped(() => { }, materializer);

                //give the system enough time to shutdown and release the file handle
                Thread.Sleep(500);
                targetFile.Delete();
            }
        }

        private static void CheckFileContent(FileInfo f, string contents)
        {
            using (var s = f.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using(var reader = new StreamReader(s))
                {
                    var cont = reader.ReadToEnd();
                    cont.Should().Be(contents);
                }
            }
        }
    }
}
