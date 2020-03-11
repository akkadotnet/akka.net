//-----------------------------------------------------------------------
// <copyright file="FlowAskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowAskSpec : AkkaSpec
    {
        #region internal classes

        sealed class Reply : IEquatable<Reply>
        {
            public int Payload { get; }

            public Reply(int payload)
            {
                Payload = payload;
            }

            public bool Equals(Reply other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Payload == other.Payload;
            }

            public override bool Equals(object obj) => obj is Reply reply && Equals(reply);
            public override int GetHashCode() => Payload;

            public override string ToString() => $"Reply({Payload})";
        }

        sealed class Replier : UntypedActor
        {
            protected override void OnReceive(object message) => Sender.Tell(new Reply((int)message));
        }

        sealed class ReplyAndProxy : ReceiveActor
        {
            public ReplyAndProxy(IActorRef to)
            {
                Receive<int>(msg =>
                {
                    to.Tell(msg);
                    Sender.Tell(new Reply(msg));
                });
            }
        }

        sealed class RandomDelaysReplier : ReceiveActor
        {
            public RandomDelaysReplier()
            {
                Receive<int>(msg =>
                {
                    var replyTo = Sender;
                    Task.Run(async () =>
                    {
                        await Task.Delay(ThreadLocalRandom.Current.Next(1, 10));
                        replyTo.Tell(new Reply(msg));
                    });
                });
            }
        }

        sealed class StatusReplier : UntypedActor
        {
            protected override void OnReceive(object message) =>
                Sender.Tell(new Status.Success(new Reply((int)message)));
        }

        sealed class FailOn : ReceiveActor
        {
            public FailOn(int n)
            {
                var log = Context.GetLogger();
                Receive<int>(msg =>
                {
                    log.Info("Incoming message [{0}] of type [{1}]", n, n.GetType());
                    if (msg == n)
                        Sender.Tell(new Status.Failure(new Exception($"Booming for {n}!")));
                    else
                        Sender.Tell(new Status.Success(new Reply(msg)));
                });
            }
        }

        sealed class FailOnAllExcept : ReceiveActor
        {
            public FailOnAllExcept(int n)
            {
                Receive<int>(msg =>
                {
                    if (msg == n)
                        Sender.Tell(new Status.Success(new Reply(msg)));
                    else
                        Sender.Tell(new Status.Failure(new Exception($"Booming for {n}!")));
                });
            }
        }

        #endregion

        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
            akka.test.stream-dispatcher {
              type = Dispatcher
              executor = ""fork-join-executor""
              fork-join-executor {
                parallelism-min = 8
                parallelism-max = 8
              }
              mailbox-requirement = ""Akka.Dispatch.IUnboundedMessageQueueSemantics""
            }
            
            akka.stream {
              materializer {
                dispatcher = ""akka.test.stream-dispatcher""
              }
            }"); 
        
        private readonly IMaterializer _materializer;
        private readonly TimeSpan _timeout = 10.Seconds();

        public FlowAskSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void Flow_with_ask_must_produce_asked_elements() => this.AssertAllStagesStopped(() =>
        {
            var replyOnInts =
                Sys.ActorOf(Props.Create(() => new Replier()).WithDispatcher("akka.test.stream-dispatcher"),
                    "replyOnInts");
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 3))
                .Ask<Reply>(replyOnInts, _timeout, 4)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            var sub = c.ExpectSubscription();
            sub.Request(2);
            c.ExpectNext(new Reply(1));
            c.ExpectNext(new Reply(2));
            c.ExpectNoMsg(200.Milliseconds());
            sub.Request(2);
            c.ExpectNext(new Reply(3));
            c.ExpectComplete();
        }, _materializer);

        [Fact]
        public void Flow_with_ask_must_produce_asked_elements_for_simple_ask() => this.AssertAllStagesStopped(() =>
        {
            var replyOnInts = Sys.ActorOf(Props.Create(() => new Replier()).WithDispatcher("akka.test.stream-dispatcher"), "replyOnInts");
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 3))
                .Ask<Reply>(replyOnInts, _timeout)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            var sub = c.ExpectSubscription();
            sub.Request(2);
            c.ExpectNext(new Reply(1));
            c.ExpectNext(new Reply(2));
            c.ExpectNoMsg(200.Milliseconds());
            sub.Request(2);
            c.ExpectNext(new Reply(3));
            c.ExpectComplete();
        }, _materializer);

        [Fact]
        public void Flow_with_ask_must_produce_asked_elements_when_response_is_Status_Success() => this.AssertAllStagesStopped(() =>
        {
            var statusReplier = Sys.ActorOf(Props.Create(() => new StatusReplier()).WithDispatcher("akka.test.stream-dispatcher"), "statusReplier");
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 3))
                .Ask<Reply>(statusReplier, _timeout, 4)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            var sub = c.ExpectSubscription();
            sub.Request(2);
            c.ExpectNext(new Reply(1));
            c.ExpectNext(new Reply(2));
            c.ExpectNoMsg(200.Milliseconds());
            sub.Request(2);
            c.ExpectNext(new Reply(3));
            c.ExpectComplete();
        }, _materializer);

        [Fact]
        public void Flow_with_ask_must_produce_future_elements_in_order()
        {
            var replyRandomDelays = Sys.ActorOf(Props.Create(() => new RandomDelaysReplier()).WithDispatcher("akka.test.stream-dispatcher"), "replyRandomDelays");
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 50))
                .Ask<Reply>(replyRandomDelays, _timeout, 4)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            var sub = c.ExpectSubscription();
            sub.Request(1000);
            for (int i = 1; i <= 50; i++)
                c.ExpectNext(new Reply(i));

            c.ExpectComplete();
        }

        [Fact]
        public void Flow_with_ask_must_signal_ask_timeout_failure() => this.AssertAllStagesStopped(() =>
        {
            var dontReply = Sys.ActorOf(BlackHoleActor.Props.WithDispatcher("akka.test.stream-dispatcher"), "dontReply");
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 50))
                .Ask<Reply>(dontReply, 10.Milliseconds(), 4)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            c.ExpectSubscription().Request(10);
            var error = c.ExpectError();
            error.As<AggregateException>().Flatten()
                .InnerException
                .Should().BeOfType<AskTimeoutException>();
        }, _materializer);

        [Fact(Skip = "Racy on Azure DevOps")]
        public void Flow_with_ask_must_signal_ask_failure() => this.AssertAllStagesStopped(() =>
        {
            var failsOn = ReplierFailOn(1);
            var c = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.From(Enumerable.Range(1, 5))
                .Ask<Reply>(failsOn, _timeout, 4)
                .RunWith(Sink.FromSubscriber(c), _materializer);

            c.ExpectSubscription().Request(10);
            var error = c.ExpectError().As<AggregateException>();
            error.Flatten().InnerException.Message.Should().Be("Booming for 1!");
        }, _materializer);

        [Fact]
        public void Flow_with_ask_signal_failure_when_target_actor_is_terminated() => this.AssertAllStagesStopped(() =>
        {
            var r = Sys.ActorOf(Props.Create(() => new Replier()).WithDispatcher("akka.test.stream-dispatcher"), "replyRandomDelays");
            var done = Source.Maybe<int>()
                .Ask<Reply>(r, _timeout, 4)
                .RunWith(Sink.Ignore<Reply>(), _materializer);

            Intercept<AggregateException>(() =>
            {
                r.Tell(PoisonPill.Instance);
                done.Wait(RemainingOrDefault);
            })
            .Flatten()
            .InnerException.Should().BeOfType<WatchedActorTerminatedException>();

        }, _materializer);

        [Fact]
        public void Flow_with_ask_a_failure_mid_stream_must_skip_element_with_resume_strategy() => this.AssertAllStagesStopped(() =>
        {
            var p = CreateTestProbe();
            var input = new[] { "a", "b", "c", "d", "e", "f" };
            var elements = Source.From(input)
                .Ask<string>(p.Ref, _timeout, 5)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Supervision.Deciders.ResumingDecider))
                .RunWith(Sink.Seq<string>(), _materializer);

            // the problematic ordering:
            p.ExpectMsg("a");
            p.LastSender.Tell("a");

            p.ExpectMsg("b");
            p.LastSender.Tell("b");

            p.ExpectMsg("c");
            var cSender = p.LastSender;

            p.ExpectMsg("d");
            p.LastSender.Tell("d");

            p.ExpectMsg("e");
            p.LastSender.Tell("e");

            p.ExpectMsg("f");
            p.LastSender.Tell("f");

            cSender.Tell(new Status.Failure(new Exception("Boom!")));
            elements.Result.Should().BeEquivalentTo(new[] { "a", "b", /*no c*/ "d", "e", "f" });

        }, _materializer);

        [Fact]
        public void Flow_with_ask_must_resume_after_ask_failure() => this.AssertAllStagesStopped(() =>
        {
            var c = this.CreateManualSubscriberProbe<Reply>();
            var aref = ReplierFailOn(3);
            var p = Source.From(Enumerable.Range(1, 5))
                .Ask<Reply>(aref, _timeout, 4)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Supervision.Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(c))
                .Run(_materializer);

            var sub = c.ExpectSubscription();
            sub.Request(10);
            foreach (var i in new[] { 1, 2, 4, 5 })
            {
                c.ExpectNext(new Reply(i));
            }

            c.ExpectComplete();

        }, _materializer);

        [Fact]
        public void Flow_with_ask_must_resume_after_multiple_failures() => this.AssertAllStagesStopped(() =>
        {
            var aref = ReplierFailAllExceptOn(6);
            var t = Source.From(Enumerable.Range(1, 6))
                .Ask<Reply>(aref, _timeout, 2)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Supervision.Deciders.ResumingDecider))
                .RunWith(Sink.First<Reply>(), _materializer);

            t.Wait(3.Seconds()).Should().BeTrue();
            t.Result.Should().Be(new Reply(6));
        }, _materializer);

        [Fact]
        public void Flow_with_ask_should_handle_cancel_properly() => this.AssertAllStagesStopped(() =>
        {
            var dontReply = Sys.ActorOf(BlackHoleActor.Props.WithDispatcher("akka.test.stream-dispatcher"), "dontReply");
            var pub = this.CreateManualPublisherProbe<int>();
            var sub = this.CreateManualSubscriberProbe<Reply>();

            var p = Source.FromPublisher(pub)
                .Ask<Reply>(dontReply, _timeout, 4)
                .RunWith(Sink.FromSubscriber(sub), _materializer);

            var upstream = pub.ExpectSubscription();
            upstream.ExpectRequest();
            sub.ExpectSubscription().Cancel();
            upstream.ExpectCancellation();
        }, _materializer);

        private IActorRef ReplierFailOn(int n) => 
            Sys.ActorOf(Props.Create(() => new FailOn(n)).WithDispatcher("akka.test.stream-dispatcher"), 
                "failureReplier-" + n);

        private IActorRef ReplierFailAllExceptOn(int n) => 
            Sys.ActorOf(Props.Create(() => new FailOnAllExcept(n)).WithDispatcher("akka.test.stream-dispatcher"), 
                "failureReplier-" + n);
    }
}
