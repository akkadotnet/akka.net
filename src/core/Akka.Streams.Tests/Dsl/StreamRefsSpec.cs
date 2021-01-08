//-----------------------------------------------------------------------
// <copyright file="StreamRefsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    internal sealed class DataSourceActor : ActorBase
    {
        public static Props Props(IActorRef probe) =>
            Akka.Actor.Props.Create(() => new DataSourceActor(probe));//.WithDispatcher("akka.test.stream-dispatcher");

        private readonly IActorRef _probe;
        private readonly ActorMaterializer _materializer;

        public DataSourceActor(IActorRef probe)
        {
            _probe = probe;
            _materializer = Context.System.Materializer();
        }

        protected override void PostStop()
        {
            base.PostStop();
            _materializer.Dispose();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case "give":
                    {
                        /*
                         * Here we're able to send a source to a remote recipient
                         * For them it's a Source; for us it is a Sink we run data "into"
                         */
                        var source = Source.From(new[] { "hello", "world" });
                        var aref = source.RunWith(StreamRefs.SourceRef<string>(), _materializer);
                        aref.PipeTo(Sender);
                        return true;
                    }
                case "give-infinite":
                    {
                        var source = Source.From(Enumerable.Range(1, int.MaxValue).Select(i => "ping-" + i));
                        var t = source.ToMaterialized(StreamRefs.SourceRef<string>(), Keep.Right).Run(_materializer);
                        t.PipeTo(Sender);
                        return true;
                    }
                case "give-fail":
                    {
                        var r = Source.Failed<string>(new Exception("Boom!"))
                            .RunWith(StreamRefs.SourceRef<string>(), _materializer);
                        r.PipeTo(Sender);
                        return true;
                    }
                case "give-complete-asap":
                    {
                        var r = Source.Empty<string>().RunWith(StreamRefs.SourceRef<string>(), _materializer);
                        r.PipeTo(Sender);
                        return true;
                    }
                case "give-subscribe-timeout":
                    {
                        var r = Source.Repeat("is anyone there?")
                            .ToMaterialized(StreamRefs.SourceRef<string>(), Keep.Right)
                            .WithAttributes(StreamRefAttributes.CreateSubscriptionTimeout(TimeSpan.FromMilliseconds(500)))
                            .Run(_materializer);
                        r.PipeTo(Sender);
                        return true;
                    }
                case "receive":
                    {
                        /*
                         * We write out code, knowing that the other side will stream the data into it.
                         * For them it's a Sink; for us it's a Source.
                         */
                        var sink = StreamRefs.SinkRef<string>().To(Sink.ActorRef<string>(_probe, "<COMPLETE>"))
                            .Run(_materializer);
                        sink.PipeTo(Sender);
                        return true;
                    }
                case "receive-ignore":
                    {
                        var sink = StreamRefs.SinkRef<string>().To(Sink.Ignore<string>()).Run(_materializer);
                        sink.PipeTo(Sender);
                        return true;
                    }
                case "receive-subscribe-timeout":
                    {
                        var sink = StreamRefs.SinkRef<string>()
                            .WithAttributes(StreamRefAttributes.CreateSubscriptionTimeout(TimeSpan.FromMilliseconds(500)))
                            .To(Sink.ActorRef<string>(_probe, "<COMPLETE>"))
                            .Run(_materializer);
                        sink.PipeTo(Sender);
                        return true;
                    }
                case "receive-32":
                    {
                        //                    var t = StreamRefs.SinkRef<string>()
                        //                        .ToMaterialized(TestSink.SinkProbe<string>(Context.System), Keep.Both)
                        //                        .Run(_materializer);
                        //
                        //                    var sink = t.Item1;
                        //                    var driver = t.Item2;
                        //                    Task.Run(() =>
                        //                    {
                        //                        driver.EnsureSubscription();
                        //                        driver.Request(2);
                        //                        driver.ExpectNext();
                        //                        driver.ExpectNext();
                        //                        driver.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                        //                        driver.Request(30);
                        //                        driver.ExpectNextN(30);
                        //
                        //                        return "<COMPLETED>";
                        //                    }).PipeTo(_probe);

                        return true;
                    }
                default: return false;
            }
        }
    }

    internal sealed class SourceMsg
    {
        public ISourceRef<string> DataSource { get; }

        public SourceMsg(ISourceRef<string> dataSource)
        {
            DataSource = dataSource;
        }
    }

    internal sealed class BulkSourceMsg
    {
        public ISourceRef<ByteString> DataSource { get; }

        public BulkSourceMsg(ISourceRef<ByteString> dataSource)
        {
            DataSource = dataSource;
        }
    }

    internal sealed class SinkMsg
    {
        public ISinkRef<string> DataSink { get; }

        public SinkMsg(ISinkRef<string> dataSink)
        {
            DataSink = dataSink;
        }
    }

    internal sealed class BulkSinkMsg
    {
        public ISinkRef<ByteString> DataSink { get; }

        public BulkSinkMsg(ISinkRef<ByteString> dataSink)
        {
            DataSink = dataSink;
        }
    }

    public class StreamRefsSpec : AkkaSpec
    {
        public static Config Config()
        {
            var address = TestUtils.TemporaryServerAddress();
            return ConfigurationFactory.ParseString($@"        
            akka {{
              loglevel = INFO
              actor {{
                provider = remote
                serialize-messages = off
              }}
              remote.dot-netty.tcp {{
                port = {address.Port}
                hostname = ""{address.Address}""
              }}
            }}").WithFallback(ConfigurationFactory.Load());
        }

        public StreamRefsSpec(ITestOutputHelper output) : this(Config(), output: output)
        {
        }

        protected StreamRefsSpec(Config config, ITestOutputHelper output = null) : base(config, output)
        {
            Materializer = Sys.Materializer();
            RemoteSystem = ActorSystem.Create("remote-system", Config());
            InitializeLogger(RemoteSystem);
            _probe = CreateTestProbe();

            var it = RemoteSystem.ActorOf(DataSourceActor.Props(_probe.Ref), "remoteActor");
            var remoteAddress = ((ActorSystemImpl)RemoteSystem).Provider.DefaultAddress;
            Sys.ActorSelection(it.Path.ToStringWithAddress(remoteAddress)).Tell(new Identify("hi"));

            _remoteActor = ExpectMsg<ActorIdentity>().Subject;
        }

        protected readonly ActorSystem RemoteSystem;
        protected readonly ActorMaterializer Materializer;
        private readonly TestProbe _probe;
        private readonly IActorRef _remoteActor;

        protected override void BeforeTermination()
        {
            base.BeforeTermination();
            RemoteSystem.Dispose();
            Materializer.Dispose();
        }

        [Fact]
        public void SourceRef_must_send_messages_via_remoting()
        {
            _remoteActor.Tell("give");
            var sourceRef = ExpectMsg<ISourceRef<string>>();

            sourceRef.Source.RunWith(Sink.ActorRef<string>(_probe.Ref, "<COMPLETE>"), Materializer);

            _probe.ExpectMsg("hello");
            _probe.ExpectMsg("world");
            _probe.ExpectMsg("<COMPLETE>");
        }

        [Fact]
        public void SourceRef_must_fail_when_remote_source_failed()
        {
            _remoteActor.Tell("give-fail");
            var sourceRef = ExpectMsg<ISourceRef<string>>();

            sourceRef.Source.RunWith(Sink.ActorRef<string>(_probe.Ref, "<COMPLETE>"), Materializer);

            var f = _probe.ExpectMsg<Status.Failure>();
            f.Cause.Message.Should().Contain("Remote stream (");
            f.Cause.Message.Should().Contain("Boom!");
        }

        [Fact]
        public void SourceRef_must_complete_properly_when_remote_source_is_empty()
        {
            // this is a special case since it makes sure that the remote stage is still there when we connect to it
            _remoteActor.Tell("give-complete-asap");
            var sourceRef = ExpectMsg<ISourceRef<string>>();

            sourceRef.Source.RunWith(Sink.ActorRef<string>(_probe.Ref, "<COMPLETE>"), Materializer);

            _probe.ExpectMsg("<COMPLETE>");
        }

        [Fact]
        public void SourceRef_must_respect_backpressure_from_implied_by_target_Sink()
        {
            _remoteActor.Tell("give-infinite");
            var sourceRef = ExpectMsg<ISourceRef<string>>();

            var probe = sourceRef.Source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.EnsureSubscription();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            probe.Request(1);
            probe.ExpectNext("ping-1");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            probe.Request(20);
            probe.ExpectNextN(Enumerable.Range(1, 20).Select(i => "ping-" + (i + 1)));
            probe.Cancel();

            // since no demand anyway
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            // should not cause more pulling, since we issued a cancel already
            probe.Request(10);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void SourceRef_must_receive_timeout_if_subscribing_too_late_to_the_source_ref()
        {
            _remoteActor.Tell("give-subscribe-timeout");
            var sourceRef = ExpectMsg<ISourceRef<string>>();


            // not materializing it, awaiting the timeout...
            Thread.Sleep(800);

            var probe = sourceRef.Source.RunWith(this.SinkProbe<string>(), Materializer);

            // the local "remote sink" should cancel, since it should notice the origin target actor is dead
            probe.EnsureSubscription();
            var ex = probe.ExpectError();
            ex.Message.Should().Contain("has terminated unexpectedly");
        }

        [Fact(Skip ="Racy")]
        public void SourceRef_must_not_receive_subscription_timeout_when_got_subscribed()
        {
            _remoteActor.Tell("give-subscribe-timeout");
            var remoteSource = ExpectMsg<ISourceRef<string>>();
            // materialize directly and start consuming, timeout is 500ms
            var eventualString = remoteSource.Source
                .Throttle(1, 100.Milliseconds(), 1, ThrottleMode.Shaping)
                .Take(60)
                .RunWith(Sink.Seq<string>(), Materializer);

            eventualString.Wait(8.Seconds()).Should().BeTrue();
        }

        [Fact]
        public void SourceRef_must_not_receive_timeout_when_data_is_being_sent()
        {
            _remoteActor.Tell("give-infinite");
            var remoteSource = ExpectMsg<ISourceRef<string>>();

            var done = remoteSource.Source
                .Throttle(1, 200.Milliseconds(), 1, ThrottleMode.Shaping)
                .TakeWithin(5.Seconds()) // which is > than the subscription timeout (so we make sure the timeout was cancelled
                .RunWith(Sink.Seq<string>(), Materializer);

            done.Wait(8.Seconds()).Should().BeTrue();
        }

        [Fact]
        public void SinkRef_must_receive_elements_via_remoting()
        {
            _remoteActor.Tell("receive");
            var remoteSink = ExpectMsg<ISinkRef<string>>();

            Source.From(new[] { "hello", "world" })
                .To(remoteSink.Sink)
                .Run(Materializer);

            _probe.ExpectMsg("hello");
            _probe.ExpectMsg("world");
            _probe.ExpectMsg("<COMPLETE>");
        }

        [Fact]
        public void SinkRef_must_fail_origin_if_remote_Sink_gets_a_failure()
        {
            _remoteActor.Tell("receive");
            var remoteSink = ExpectMsg<ISinkRef<string>>();

            Source.Failed<string>(new Exception("Boom!"))
                .To(remoteSink.Sink)
                .Run(Materializer);

            var failure = _probe.ExpectMsg<Status.Failure>();
            failure.Cause.Message.Should().Contain("Remote stream (");
            failure.Cause.Message.Should().Contain("Boom!");
        }

        [Fact]
        public void SinkRef_must_receive_hundreds_of_elements_via_remoting()
        {
            _remoteActor.Tell("receive");
            var remoteSink = ExpectMsg<ISinkRef<string>>();

            var msgs = Enumerable.Range(1, 100).Select(i => "payload-" + i).ToArray();

            Source.From(msgs).RunWith(remoteSink.Sink, Materializer);

            foreach (var msg in msgs)
            {
                _probe.ExpectMsg(msg);
            }

            _probe.ExpectMsg("<COMPLETE>");
        }

        [Fact]
        public void SinkRef_must_receive_timeout_if_subscribing_too_late_to_the_sink_ref()
        {
            _remoteActor.Tell("receive-subscribe-timeout");
            var remoteSink = ExpectMsg<ISinkRef<string>>();

            // not materializing it, awaiting the timeout...
            Thread.Sleep(800);

            var probe = this.SourceProbe<string>().To(remoteSink.Sink).Run(Materializer);

            var failure = _probe.ExpectMsg<Status.Failure>();
            failure.Cause.Message.Should().Contain("Remote side did not subscribe (materialize) handed out Sink reference");

            // the local "remote sink" should cancel, since it should notice the origin target actor is dead
            probe.ExpectCancellation();
        }

        [Fact(Skip ="Racy")]
        public void SinkRef_must_not_receive_timeout_if_subscribing_is_already_done_to_the_sink_ref()
        {
            _remoteActor.Tell("receive-subscribe-timeout");
            var remoteSink = ExpectMsg<ISinkRef<string>>();
            Source.Repeat("whatever")
                .Throttle(1, 100.Milliseconds(), 1, ThrottleMode.Shaping)
                .Take(10)
                .RunWith(remoteSink.Sink, Materializer);

            for (int i = 0; i < 10; i++)
            {
                _probe.ExpectMsg("whatever");
            }

            _probe.ExpectMsg("<COMPLETE>");
        }

        [Fact]
        public void SinkRef_must_not_receive_timeout_while_data_is_being_sent()
        {
            _remoteActor.Tell("receive-ignore");
            var remoteSink = ExpectMsg<ISinkRef<string>>();

            var done =
                Source.Repeat("hello-24934")
                    .Throttle(1, 300.Milliseconds(), 1, ThrottleMode.Shaping)
                    .TakeWithin(5.Seconds()) // which is > than the subscription timeout (so we make sure the timeout was cancelled)
                    .AlsoToMaterialized(Sink.Last<string>(), Keep.Right)
                    .To(remoteSink.Sink)
                    .Run(Materializer);

            done.Wait(8.Seconds()).Should().BeTrue();

        }

        [Fact(Skip = "FIXME: how to pass test assertions to remote system?")]
        public void SinkRef_must_respect_backpressure_implied_by_origin_Sink()
        {
            _remoteActor.Tell("receive-32");
            var sinkRef = ExpectMsg<ISinkRef<string>>();

            Source.Repeat("hello").RunWith(sinkRef.Sink, Materializer);

            // if we get this message, it means no checks in the request/expect semantics were broken, good!
            _probe.ExpectMsg("<COMPLETED>");
        }

        [Fact]
        public void SinkRef_must_not_allow_materializing_multiple_times()
        {
            _remoteActor.Tell("receive-subscribe-timeout");
            var sinkRef = ExpectMsg<ISinkRef<string>>();

            var p1 = this.SourceProbe<string>().To(sinkRef.Sink).Run(Materializer);
            var p2 = this.SourceProbe<string>().To(sinkRef.Sink).Run(Materializer);

            p1.EnsureSubscription();
            var req = p1.ExpectRequest();

            // will be cancelled immediately, since it's 2nd:
            p2.EnsureSubscription();
            p2.ExpectCancellation();
        }
    }
}
