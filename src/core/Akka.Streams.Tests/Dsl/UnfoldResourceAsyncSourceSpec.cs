﻿//-----------------------------------------------------------------------
// <copyright file="UnfoldResourceAsyncSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class UnfoldResourceAsyncSourceSpec : AkkaSpec
    {
        private static int _counter;

        private static string CreateLine(char c) => Enumerable.Repeat(c, 100).Aggregate("", (s, c1) => s + c1) + "\n";

        private static readonly string ManyLines =
            new[] { 'a', 'b', 'c', 'd', 'e', 'f' }.SelectMany(c => Enumerable.Repeat(CreateLine(c), 10))
                .Aggregate("", (s, s1) => s + s1);

        private static readonly string[] ManyLinesArray = ManyLines.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

        private readonly FileInfo _manyLinesFile;
        private readonly Func<Task<StreamReader>> _open;

        private static readonly Func<StreamReader, Task<Option<string>>> Read =
            reader => Task.FromResult(reader.ReadLine() ?? Option<string>.None);

        private static readonly Func<StreamReader, Task> Close = reader =>
        {
            reader.Dispose();
            return Task.FromResult(NotUsed.Instance);
        };

        public UnfoldResourceAsyncSourceSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            _open = () => Task.FromResult(new StreamReader(_manyLinesFile.OpenRead()));

            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            Materializer = Sys.Materializer(settings);

            _manyLinesFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"blocking-source-spec-{_counter++}.tmp"));
            if (_manyLinesFile.Exists)
                _manyLinesFile.Delete();

            using (var stream = _manyLinesFile.CreateText())
                stream.Write(ManyLines);
        }

        public ActorMaterializer Materializer { get; }


        [Fact]
        public void A_UnfoldResourceAsyncSource_must_read_contents_from_a_file()
        {
            this.AssertAllStagesStopped(() =>
            {
                var createPromise = new TaskCompletionSource<StreamReader>();
                var readPromise = new TaskCompletionSource<Option<string>>();
                var closePromise = new TaskCompletionSource<NotUsed>();

                var createPromiseCalled = new TaskCompletionSource<NotUsed>();
                var readPromiseCalled = new TaskCompletionSource<NotUsed>();
                var closePromiseCalled = new TaskCompletionSource<NotUsed>();

                var resource = new StreamReader(_manyLinesFile.OpenRead());
                var p = Source.UnfoldResourceAsync(
                    create: () =>
                    {
                        createPromiseCalled.SetResult(NotUsed.Instance);
                        return createPromise.Task;
                    },
                    read: reader =>
                    {
                        readPromiseCalled.SetResult(NotUsed.Instance);
                        return readPromise.Task;
                    },
                    close: reader =>
                    {
                        closePromiseCalled.SetResult(NotUsed.Instance);
                        return closePromise.Task;
                    }).RunWith(Sink.AsPublisher<string>(false), Materializer);

                var c = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                sub.Request(1);
                createPromiseCalled.Task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                createPromise.SetResult(resource);

                readPromiseCalled.Task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                readPromise.SetResult(resource.ReadLine());
                c.ExpectNext().Should().Be(ManyLinesArray[0]);

                sub.Cancel();
                closePromiseCalled.Task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                resource.Dispose();
                closePromise.SetResult(NotUsed.Instance);
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_close_resource_successfully_right_after_open()
        {
            this.AssertAllStagesStopped(() =>
            {
                var createPromise = new TaskCompletionSource<StreamReader>();
                var readPromise = new TaskCompletionSource<Option<string>>();
                var closePromise = new TaskCompletionSource<NotUsed>();

                var createPromiseCalled = new TaskCompletionSource<NotUsed>();
                var readPromiseCalled = new TaskCompletionSource<NotUsed>();
                var closePromiseCalled = new TaskCompletionSource<NotUsed>();

                var resource = new StreamReader(_manyLinesFile.OpenRead());
                var p = Source.UnfoldResourceAsync(
                    create: () =>
                    {
                        createPromiseCalled.SetResult(NotUsed.Instance);
                        return createPromise.Task;
                    },
                    read: reader =>
                    {
                        readPromiseCalled.SetResult(NotUsed.Instance);
                        return readPromise.Task;
                    },
                    close: reader =>
                    {
                        closePromiseCalled.SetResult(NotUsed.Instance);
                        return closePromise.Task;
                    }).RunWith(Sink.AsPublisher<string>(false), Materializer);

                var c = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                createPromiseCalled.Task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                createPromise.SetResult(resource);

                sub.Cancel();
                closePromiseCalled.Task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                resource.Dispose();
                closePromise.SetResult(NotUsed.Instance);
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_continue_when_strategy_is_resume_and_exception_happened()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = Source.UnfoldResourceAsync(_open, reader =>
                {
                    var s = reader.ReadLine();
                    if (s != null && s.Contains("b"))
                        throw new TestException("");
                    return Task.FromResult(s ?? Option<string>.None);
                }, Close)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<string>();

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                Enumerable.Range(0, 50).ForEach(i =>
                 {
                     sub.Request(1);
                     c.ExpectNext().Should().Be(i < 10 ? ManyLinesArray[i] : ManyLinesArray[i + 10]);
                 });
                sub.Request(1);
                c.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_close_and_open_stream_again_when_strategy_is_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = Source.UnfoldResourceAsync(_open, reader =>
                {
                    var s = reader.ReadLine();
                    if (s != null && s.Contains("b"))
                        throw new TestException("");
                    return Task.FromResult<Option<string>>(s);
                }, Close)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<string>();

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                Enumerable.Range(0, 20).ForEach(i =>
                {
                    sub.Request(1);
                    c.ExpectNext().Should().Be(ManyLinesArray[0]);
                });
                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_work_with_ByteString_as_well()
        {
            this.AssertAllStagesStopped(() =>
            {
                var chunkSize = 50;
                var buffer = new char[chunkSize];

                var p = Source.UnfoldResourceAsync(_open, reader =>
                {
                    var promise = new TaskCompletionSource<Option<ByteString>>();
                    var s = reader.Read(buffer, 0, chunkSize);

                    promise.SetResult(s > 0
                        ? ByteString.FromString(buffer.Aggregate("", (s1, c1) => s1 + c1)).Slice(0, s)
                        : Option<ByteString>.None);
                    return promise.Task;

                }, reader =>
                {
                    reader.Dispose();
                    return Task.FromResult(NotUsed.Instance);
                })
                .RunWith(Sink.AsPublisher<ByteString>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<ByteString>();

                var remaining = ManyLines;
                Func<string> nextChunk = () =>
                {
                    if (remaining.Length <= chunkSize)
                        return remaining;
                    var chunk = remaining.Take(chunkSize).Aggregate("", (s, c1) => s + c1);
                    remaining = remaining.Substring(chunkSize);
                    return chunk;
                };

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                Enumerable.Range(0, 122).ForEach(i =>
                {
                    sub.Request(1);
                    c.ExpectNext().ToString().Should().Be(nextChunk());
                });
                sub.Request(1);
                c.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_use_dedicated_blocking_io_dispatcher_by_default()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
                var materializer = sys.Materializer();

                try
                {
                    var p = Source.UnfoldResourceAsync(_open, Read, Close)
                        .RunWith(this.SinkProbe<string>(), materializer);

                    ((ActorMaterializerImpl)materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance,
                        TestActor);
                    var refs = ExpectMsg<StreamSupervisor.Children>().Refs;
                    var actorRef = refs.First(@ref => @ref.Path.ToString().Contains("unfoldResourceSourceAsync"));
                    try
                    {
                        Utils.AssertDispatcher(actorRef, ActorAttributes.IODispatcher.Name);
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
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_fail_when_create_throws_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new TestException("");
                var p = Source.UnfoldResourceAsync(() =>
                {
                    throw testException;
                }, Read, Close).RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c);

                c.ExpectSubscription();
                c.ExpectError().Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_fail_when_close_throws_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new TestException("");
                var p = Source.UnfoldResourceAsync(_open, Read, reader =>
                {
                    reader.Dispose();
                    throw testException;
                }).RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c);

                var sub = c.ExpectSubscription();
                sub.Request(61);
                c.ExpectNextN(60);
                c.ExpectError().Should().Be(testException);

            }, Materializer);
        }

        [Fact]
        public void A_UnfoldResourceAsyncSource_must_close_resource_when_stream_is_abruptly_termianted()
        {
            var closeLatch = new TestLatch(1);
            var materializer = ActorMaterializer.Create(Sys);
            var p = Source.UnfoldResourceAsync(_open, Read, reader =>
            {
                closeLatch.CountDown();
                return Task.FromResult(0);
            }).RunWith(Sink.AsPublisher<string>(false), materializer);
            var c = this.CreateManualSubscriberProbe<string>();
            p.Subscribe(c);
            materializer.Shutdown();
            closeLatch.Ready(TimeSpan.FromSeconds(10));
        }

        protected override void AfterAll()
        {
            base.AfterAll();

            try
            {
                _manyLinesFile.Delete();
            }
            catch
            {
                Thread.Sleep(3000);
                try
                {
                    _manyLinesFile.Delete();
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}
