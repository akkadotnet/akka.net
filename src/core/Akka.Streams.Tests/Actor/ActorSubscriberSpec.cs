//-----------------------------------------------------------------------
// <copyright file="ActorSubscriberSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Actor
{
    public class ActorSubscriberSpec : AkkaSpec
    {
        public ActorSubscriberSpec(ITestOutputHelper helper)
            : base(
                AkkaSpecConfig.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
                helper)
        {

        }

        [Fact]
        public async Task ActorSubscriber_should_receive_requested_elements()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var actorRef = Source.From(new[] { 1, 2, 3 })
                    .RunWith(Sink.ActorSubscriber<int>(ManualSubscriber.Props(TestActor)), materializer);

                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready"); //requesting 2
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(3);
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriber_should_signal_error()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var e = new Exception("simulated");
                var actorRef = Source.FromEnumerator<int>(() => throw e)
                    .RunWith(Sink.ActorSubscriber<int>(ManualSubscriber.Props(TestActor)), materializer);
                actorRef.Tell("ready");

                (await ExpectMsgAsync<OnError>()).Cause.Should().Be(e);
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_remember_requested_after_restart()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // creating actor with default supervision, because stream supervisor default strategy is to 
                var actorRef = Sys.ActorOf(ManualSubscriber.Props(TestActor));
                Source.From(Enumerable.Range(1, 7))
                    .RunWith(Sink.FromSubscriber(new ActorSubscriberImpl<int>(actorRef)), materializer);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                await ExpectNoMsgAsync(200);
                actorRef.Tell("boom");
                actorRef.Tell("ready");
                actorRef.Tell("ready");
                actorRef.Tell("boom");
                foreach (var n in Enumerable.Range(3, 4))
                {
                    (await ExpectMsgAsync<OnNext>()).Element.Should().Be(n);
                }
                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(7);
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_not_deliver_more_after_cancel()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var actorRef = Source.From(Enumerable.Range(1, 5))
                    .RunWith(Sink.ActorSubscriber<int>(ManualSubscriber.Props(TestActor)), materializer);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                actorRef.Tell("requestAndCancel");
                await ExpectNoMsgAsync(200);
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_terminate_after_cancel()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var actorRef = Source.From(Enumerable.Range(1, 5))
                    .RunWith(Sink.ActorSubscriber<int>(ManualSubscriber.Props(TestActor)), materializer);
                Watch(actorRef);
                actorRef.Tell("requestAndCancel");
                await ExpectTerminatedAsync(actorRef);
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_cancel_incoming_subscription_when_cancel_was_called_before_it_arrived()
        {
            var actorRef = Sys.ActorOf(ImmediatelyCancelledSubscriber.Props(TestActor));
            var sub = new ActorSubscriberImpl<object>(actorRef);
            Watch(actorRef);
            await ExpectNoMsgAsync(200);
            sub.OnSubscribe(new CancelSubscription(TestActor));
            await ExpectMsgAsync("cancel");
            await ExpectTerminatedAsync(actorRef, TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_work_with_OneByOneRequestStrategy()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                Source.From(Enumerable.Range(1, 17))
                    .RunWith(Sink.ActorSubscriber<int>(RequestStrategySubscriber.Props(TestActor, OneByOneRequestStrategy.Instance)), materializer);
                foreach (var n in Enumerable.Range(1, 17))
                {
                    (await ExpectMsgAsync<OnNext>()).Element.Should().Be(n);
                }
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_should_work_with_WatermarkRequestStrategy()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                Source.From(Enumerable.Range(1, 17))
                    .RunWith(Sink.ActorSubscriber<int>(RequestStrategySubscriber.Props(TestActor, new WatermarkRequestStrategy(highWatermark: 10))), materializer);
                foreach (var n in Enumerable.Range(1, 17))
                {
                    (await ExpectMsgAsync<OnNext>()).Element.Should().Be(n);
                }
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }

        [Fact]
        public async Task ActorSubscriberSpec_should_support_custom_max_in_flight_request_strategy_with_child_workers()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                const int n = 117;
                Source.From(Enumerable.Range(1, n))
                    .Select(i => new Msg(i, TestActor))
                    .RunWith(Sink.ActorSubscriber<Msg>(Streamer.Props), Sys.Materializer());
                (await ReceiveNAsync(n).ToListAsync())
                    .Should().BeEquivalentTo(Enumerable.Range(1, n).Select(i => new Done(i)));
            }, materializer);
        }

    }


    internal class ManualSubscriber : ActorSubscriber
    {
        public static Props Props(IActorRef probe)
            => Akka.Actor.Props.Create(() => new ManualSubscriber(probe)).WithDispatcher("akka.test.stream-dispatcher");

        private readonly IActorRef _probe;

        public ManualSubscriber(IActorRef probe)
        {
            _probe = probe;
        }

        public override IRequestStrategy RequestStrategy { get; } = ZeroRequestStrategy.Instance;

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext msg:
                    _probe.Tell(msg);
                    return true;
                case OnComplete msg:
                    _probe.Tell(msg);
                    return true;
                case OnError msg:
                    _probe.Tell(msg);
                    return true;
                case string s:
                    switch (s)
                    {
                        case "ready":
                            Request(2);
                            break;
                        case "boom":
                            throw new Exception(s);
                        case "requestAndCancel":
                            Request(1);
                            Cancel();
                            break;
                        case "cancel":
                            Cancel();
                            break;
                    }
                    return true;
                default:
                    return false;
            }
        }
    }

    internal class ImmediatelyCancelledSubscriber : ManualSubscriber
    {
        public new static Props Props(IActorRef probe)
            => Akka.Actor.Props.Create(() => new ImmediatelyCancelledSubscriber(probe)).WithDispatcher("akka.test.stream-dispatcher");

        public ImmediatelyCancelledSubscriber(IActorRef probe) : base(probe)
        {
        }

        protected override void PreStart()
        {
            Cancel();
            base.PreStart();
        }
    }

    internal class RequestStrategySubscriber : ActorSubscriber
    {
        public static Props Props(IActorRef probe, IRequestStrategy strat)
            => Akka.Actor.Props.Create(() => new RequestStrategySubscriber(probe, strat)).WithDispatcher("akka.test.stream-dispatcher");

        private readonly IActorRef _probe;

        public RequestStrategySubscriber(IActorRef probe, IRequestStrategy strat)
        {
            _probe = probe;
            RequestStrategy = strat;
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext msg:
                    _probe.Tell(msg);
                    return true;
                case OnComplete msg:
                    _probe.Tell(msg);
                    return true;
                default:
                    return false;
            }
        }

        public override IRequestStrategy RequestStrategy { get; }
    }

    internal class CancelSubscription : ISubscription
    {
        private readonly IActorRef _probe;

        public CancelSubscription(IActorRef probe)
        {
            _probe = probe;
        }

        public void Request(long n)
        {

        }

        public void Cancel()
        {
            _probe.Tell("cancel");
        }
    }

    internal abstract class IdBase
    {
        protected IdBase(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    internal class Msg : IdBase
    {
        public Msg(int id, IActorRef replyTo) : base(id)
        {
            ReplyTo = replyTo;
        }

        public IActorRef ReplyTo { get; }
    }

    internal class Work : IdBase
    {
        public Work(int id) : base(id)
        {
        }
    }

    internal class Reply : IdBase
    {
        public Reply(int id) : base(id)
        {
        }
    }

    internal class Done : IdBase
    {
        public Done(int id) : base(id)
        {
        }
    }

    internal class Streamer : ActorSubscriber
    {
        public static Props Props { get; } = Props.Create<Streamer>().WithDispatcher("akka.test.stream-dispatcher");

        private class InFlightStrategy : MaxInFlightRequestStrategy
        {
            private readonly Streamer _streamer;

            public InFlightStrategy(Streamer streamer) : base(10)
            {
                _streamer = streamer;
            }

            public override int InFlight => _streamer._queue.Count;
        }

        private readonly Dictionary<int, IActorRef> _queue = new();
        private readonly Router _router;

        public Streamer()
        {
            RequestStrategy = new InFlightStrategy(this);

            var routees = new[]
            {
                Routee.FromActorRef(Context.ActorOf(Props.Create<Worker>().WithDispatcher(Context.Props.Dispatcher))),
                Routee.FromActorRef(Context.ActorOf(Props.Create<Worker>().WithDispatcher(Context.Props.Dispatcher))),
                Routee.FromActorRef(Context.ActorOf(Props.Create<Worker>().WithDispatcher(Context.Props.Dispatcher)))
            };
            _router = new Router(new RoundRobinRoutingLogic(), routees);
        }

        public override IRequestStrategy RequestStrategy { get; }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext next:
                    var msg = (Msg)next.Element;
                    _queue.Add(msg.Id, msg.ReplyTo);
                    if (_queue.Count > 10)
                        throw new InvalidOperationException($"queued too many: {_queue.Count}");
                    _router.Route(new Work(msg.Id), Self);
                    return true;
                case Reply reply:
                    _queue[reply.Id].Tell(new Done(reply.Id));
                    _queue.Remove(reply.Id);
                    return true;
                default:
                    return false;
            }
        }
    }

    internal class Worker : ReceiveActor
    {
        public Worker()
        {
            Receive<Work>(work => Sender.Tell(new Reply(work.Id)));
        }
    }
}
