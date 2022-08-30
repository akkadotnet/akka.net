//-----------------------------------------------------------------------
// <copyright file="ActorPublisherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ActorPublisher = Akka.Streams.Actors.ActorPublisher;
using Cancel = Akka.Streams.Actors.Cancel;

namespace Akka.Streams.Tests.Actor
{
    public class ActorPublisherSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
my-dispatcher1 {
  type = Dispatcher
  executor = ""fork-join-executor""
  fork-join-executor {
    parallelism-min = 8
    parallelism-max = 8
  }
  mailbox-requirement = ""Akka.Dispatch.IUnboundedMessageQueueSemantics""
}
my-dispatcher1 {
  type = Dispatcher
  executor = ""fork-join-executor""
  fork-join-executor {
    parallelism-min = 8
    parallelism-max = 8
  }
  mailbox-requirement = ""Akka.Dispatch.IUnboundedMessageQueueSemantics""
}");

        public ActorPublisherSpec(ITestOutputHelper output = null)
            : base(
                Config.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
                output)
        {
            EventFilter.Exception<IllegalStateException>().Mute();
        }

        [Fact]
        public async Task ActorPublisher_should_accumulate_demand()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var p = ActorPublisher.Create<string>(actorRef);
            var s = this.CreateSubscriberProbe<string>();

            p.Subscribe(s);
            await s.RequestAsync(2);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(2);
            await s.RequestAsync(3);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(5);
            await s.CancelAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_allow_onNext_up_to_requested_elements_but_not_more()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var p = ActorPublisher.Create<string>(actorRef);
            var s = this.CreateSubscriberProbe<string>();
            p.Subscribe(s);
            
            await s.RequestAsync(2);
            actorRef.Tell(new Produce("elem-1"));
            actorRef.Tell(new Produce("elem-2"));
            actorRef.Tell(new Produce("elem-3"));
            
            await s.AsyncBuilder()
                .ExpectNext("elem-1")
                .ExpectNext("elem-2")
                .ExpectNoMsg(TimeSpan.FromMilliseconds(300))
                .Cancel()
                .ExecuteAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_signal_error()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            actorRef.Tell(new Err("wrong"));
            
            await s.ExpectSubscriptionAsync();
            (await s.ExpectErrorAsync()).Message.Should().Be("wrong");
        }

        [Fact]
        public async Task ActorPublisher_should_not_terminate_after_signaling_onError()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAsync();
            probe.Watch(actorRef);
            actorRef.Tell(new Err("wrong"));
            (await s.ExpectErrorAsync()).Message.Should().Be("wrong");
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task ActorPublisher_should_terminate_after_signalling_OnErrorThenStop()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAsync();
            probe.Watch(actorRef);
            actorRef.Tell(new ErrThenStop("wrong"));
            (await s.ExpectErrorAsync()).Message.Should().Be("wrong");
            await probe.ExpectTerminatedAsync(actorRef, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task ActorPublisher_should_signal_error_before_subscribe()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            actorRef.Tell(new Err("early err"));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            (await s.ExpectSubscriptionAndErrorAsync()).Message.Should().Be("early err");
        }

        [Fact]
        public async Task ActorPublisher_should_drop_onNext_elements_after_cancel()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var p = ActorPublisher.Create<string>(actorRef);
            var s = this.CreateSubscriberProbe<string>();
            p.Subscribe(s);
            
            await s.RequestAsync(2);
            actorRef.Tell(new Produce("elem-1"));
            await s.CancelAsync();
            actorRef.Tell(new Produce("elem-2"));
            await s.ExpectNextAsync("elem-1");
            await s.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
        }

        [Fact]
        public async Task ActorPublisher_should_remember_requested_after_restart()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var p = ActorPublisher.Create<string>(actorRef);
            var s = this.CreateSubscriberProbe<string>();
            p.Subscribe(s);
            
            await s.RequestAsync(3);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(3);
            actorRef.Tell(new Produce("elem-1"));
            actorRef.Tell(Boom.Instance);
            actorRef.Tell(new Produce("elem-2"));
            await s.ExpectNextAsync("elem-1");
            await s.ExpectNextAsync("elem-2");
            await s.RequestAsync(5);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(6);
            await s.CancelAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_signal_onComplete()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.RequestAsync(3);
            actorRef.Tell(new Produce("elem-1"));
            actorRef.Tell(Complete.Instance);
            await s.ExpectNextAsync("elem-1");
            await s.ExpectCompleteAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_not_terminate_after_signalling_onComplete()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            var sub = await s.ExpectSubscriptionAsync();
            sub.Request(3);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(3);
            probe.Watch(actorRef);
            actorRef.Tell(new Produce("elem-1"));
            actorRef.Tell(Complete.Instance);
            await s.ExpectNextAsync("elem-1");
            await s.ExpectCompleteAsync();
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task ActorPublisher_should_terminate_after_signalling_onCompleteThenStop()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            var sub = await s.ExpectSubscriptionAsync();
            sub.Request(3);
            (await probe.ExpectMsgAsync<TotalDemand>()).Elements.Should().Be(3);
            probe.Watch(actorRef);
            actorRef.Tell(new Produce("elem-1"));
            actorRef.Tell(CompleteThenStop.Instance);
            await s.ExpectNextAsync("elem-1");
            await s.ExpectCompleteAsync();
            await probe.ExpectTerminatedAsync(actorRef,TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task ActorPublisher_should_signal_immediate_onComplete()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            actorRef.Tell(Complete.Instance);
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAndCompleteAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_only_allow_one_subscriber()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAsync();
            var s2 = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s2);
            
            (await s2.ExpectSubscriptionAndErrorAsync())
                .Should()
                .BeOfType<IllegalStateException>()
                .Which.Message.Should()
                .Be($"ActorPublisher {ReactiveStreamsCompliance.SupportsOnlyASingleSubscriber}");
        }

        [Fact]
        public async Task ActorPublisher_should_not_subscribe_the_same_subscriber_multiple_times()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAsync();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            (await s.ExpectErrorAsync()).Message
                .Should().Be(ReactiveStreamsCompliance.CanNotSubscribeTheSameSubscriberMultipleTimes);
        }

        [Fact]
        public async Task ActorPublisher_should_signal_onComplete_when_actor_is_stopped()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisher.Props(probe.Ref));
            var s = this.CreateManualSubscriberProbe<string>();
            ActorPublisher.Create<string>(actorRef).Subscribe(s);
            
            await s.ExpectSubscriptionAsync();
            actorRef.Tell(PoisonPill.Instance);
            await s.ExpectCompleteAsync();
        }

        [Fact]
        public async Task ActorPublisher_should_work_together_with_Flow_and_ActorSubscriber_using_old_Collect_behaviour()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();
                var source = Source.ActorPublisher<int>(Sender.Props);
                var sink = Sink.ActorSubscriber<string>(Receiver.Props(probe.Ref));
                
                var (snd, rcv) = source.Collect(n => n%2 == 0, n => "elem-" + n)
                    .ToMaterialized(sink, Keep.Both).Run(materializer);

                for (var i = 1; i <= 3; i++)
                    snd.Tell(i);
                await probe.ExpectMsgAsync("elem-2", TimeSpan.FromMinutes(10));

                for (var n = 4; n <= 500; n++)
                {
                    if (n % 19 == 0)
                        await Task.Delay(50); // simulate bursts
                    snd.Tell(n);
                }

                for (var n = 4; n <= 500; n += 2)
                    await probe.ExpectMsgAsync("elem-" + n);

                Watch(snd);
                rcv.Tell(PoisonPill.Instance);
                await ExpectTerminatedAsync(snd);
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_work_together_with_Flow_and_ActorSubscriber()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();
                var source = Source.ActorPublisher<int>(Sender.Props);
                var sink = Sink.ActorSubscriber<string>(Receiver.Props(probe.Ref));

                var (snd, rcv) = source.Collect(
                        n => n % 2 == 0, 
                        n => "elem-" + n)
                    .ToMaterialized(sink, Keep.Both).Run(materializer);

                for (var i = 1; i <= 3; i++)
                    snd.Tell(i);
                await probe.ExpectMsgAsync("elem-2", TimeSpan.FromMinutes(10));

                for (var n = 4; n <= 500; n++)
                {
                    if (n % 19 == 0)
                        await Task.Delay(50); // simulate bursts
                    snd.Tell(n);
                }

                for (var n = 4; n <= 500; n += 2)
                    await probe.ExpectMsgAsync("elem-" + n);

                Watch(snd);
                rcv.Tell(PoisonPill.Instance);
                await ExpectTerminatedAsync(snd);
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_work_in_a_GraphDsl()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe1 = CreateTestProbe();
                var probe2 = CreateTestProbe();

                var senderRef1 = ActorOf(Sender.Props);
                var source1 = Source.FromPublisher(ActorPublisher.Create<int>(senderRef1))
                    .MapMaterializedValue(_ => senderRef1);

                var sink1 = Sink.FromSubscriber(ActorSubscriber.Create<string>(ActorOf(Receiver.Props(probe1.Ref))))
                    .MapMaterializedValue(_ => probe1.Ref);
                var sink2 = Sink.ActorSubscriber<string>(Receiver.Props(probe2.Ref))
                    .MapMaterializedValue(_ => probe2.Ref);
                var senderRef2 = RunnableGraph.FromGraph(GraphDsl.Create(
                    Source.ActorPublisher<int>(Sender.Props),
                    (builder, source2) =>
                    {
                        var merge = builder.Add(new Merge<int, int>(2));
                        var bcast = builder.Add(new Broadcast<string>(2));

                        builder.From(source1).To(merge.In(0));
                        builder.From(source2.Outlet).To(merge.In(1));
                        
                        builder.From(merge.Out).Via(Flow.Create<int>().Select(i => i.ToString())).To(bcast.In);
                        
                        builder.From(bcast.Out(0)).Via(Flow.Create<string>().Select(s => s + "mark")).To(sink1);
                        builder.From(bcast.Out(1)).To(sink2);

                        return ClosedShape.Instance;
                    })).Run(materializer);

                // the scala test is wrong
                const int noOfMessages = 10;
                for (var i = 0; i < noOfMessages; i++)
                {
                    senderRef1.Tell(i);
                    senderRef2.Tell(i+noOfMessages);
                }

                var probe1Messages = new List<string>(noOfMessages*2);
                var probe2Messages = new List<string>(noOfMessages*2);
                for (var i = 0; i < noOfMessages * 2; i++)
                {
                    probe1Messages.Add(await probe1.ExpectMsgAsync<string>());
                    probe2Messages.Add(await probe2.ExpectMsgAsync<string>());
                }
                probe1Messages.Should().BeEquivalentTo(Enumerable.Range(0, noOfMessages * 2).Select(i => i + "mark"));
                probe2Messages.Should().BeEquivalentTo(Enumerable.Range(0, noOfMessages * 2).Select(i => i.ToString()));
            }, materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task ActorPublisher_should_be_able_to_define_a_subscription_timeout_after_which_it_should_shut_down()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var timeout = TimeSpan.FromMilliseconds(150);
                var a = ActorOf(TimeoutingPublisher.Props(TestActor, timeout));
                var pub = ActorPublisher.Create<int>(a);

                // don't subscribe for `timeout` millis, so it will shut itself down
                ExpectMsg("timed-out");

                // now subscribers will already be rejected, while the actor could perform some clean-up
                var sub = this.CreateManualSubscriberProbe<int>();
                pub.Subscribe(sub);
                await sub.ExpectSubscriptionAndErrorAsync();

                await ExpectMsgAsync("cleaned-up");
                // termination is triggered by user code
                Watch(a);
                await ExpectTerminatedAsync(a);
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_be_able_to_define_a_subscription_timeout_which_is_cancelled_by_the_first_incoming_Subscriber()
        {
            var timeout = TimeSpan.FromMilliseconds(500);
            var sub = this.CreateManualSubscriberProbe<int>();

            var pub = ActorPublisher.Create<int>(ActorOf(TimeoutingPublisher.Props(TestActor, timeout)));

            // subscribe right away, should cancel subscription-timeout
            pub.Subscribe(sub);
            await sub.ExpectSubscriptionAsync();

            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task ActorPublisher_should_use_dispatcher_from_materializer_settings()
        {
            var materializer = ActorMaterializer.Create(Sys, Sys.Materializer().Settings.WithDispatcher("my-dispatcher1"));
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var s = this.CreateManualSubscriberProbe<string>();
                var actorRef = Source.ActorPublisher<string>(TestPublisher.Props(TestActor, useTestDispatcher: false))
                    .To(Sink.FromSubscriber(s))
                    .Run(materializer);

                actorRef.Tell(ThreadName.Instance);
                (await ExpectMsgAsync<string>()).Should().Contain("my-dispatcher1");
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_use_dispatcher_from_operation_attributes()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var s = this.CreateManualSubscriberProbe<string>();
                var actorRef = Source.ActorPublisher<string>(TestPublisher.Props(TestActor, useTestDispatcher: false))
                    .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher1"))
                    .To(Sink.FromSubscriber(s))
                    .Run(materializer);

                actorRef.Tell(ThreadName.Instance);
                (await ExpectMsgAsync<string>()).Should().Contain("my-dispatcher1");
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_use_dispatcher_from_props()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var s = this.CreateManualSubscriberProbe<string>();
                var actorRef = Source.ActorPublisher<string>(TestPublisher.Props(TestActor, useTestDispatcher: false).WithDispatcher("my-dispatcher1"))
                    .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher2"))
                    .To(Sink.FromSubscriber(s))
                    .Run(materializer);

                actorRef.Tell(ThreadName.Instance);
                (await ExpectMsgAsync<string>()).Should().Contain("my-dispatcher1");
            }, materializer);
        }

        [Fact]
        public async Task ActorPublisher_should_handle_stash()
        {
            var probe = CreateTestProbe();
            var actorRef = Sys.ActorOf(TestPublisherWithStash.Props(probe.Ref));
            var p = new ActorPublisherImpl<string>(actorRef);
            var s = this.CreateSubscriberProbe<string>();
            p.Subscribe(s);
            
            await s.RequestAsync(2);
            await s.RequestAsync(3);
            actorRef.Tell("unstash");
            await probe.ExpectMsgAsync(new TotalDemand(5));
            await probe.ExpectMsgAsync(new TotalDemand(5));
            await s.RequestAsync(4);
            await probe.ExpectMsgAsync(new TotalDemand(9));
            await s.CancelAsync();
        }
    }

    internal class TestPublisher : Actors.ActorPublisher<string>
    {
        public static Props Props(IActorRef probe, bool useTestDispatcher = true)
        {
            var p = Akka.Actor.Props.Create(() => new TestPublisher(probe));
            return useTestDispatcher ? p.WithDispatcher("akka.test.stream-dispatcher") : p;
        }

        private readonly IActorRef _probe;
        
        public TestPublisher(IActorRef probe)
        {
            _probe = probe;
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request request:
                    _probe.Tell(new TotalDemand(TotalDemand));
                    return true;
                case Produce produce:
                    OnNext(produce.Elem);
                    return true;
                case Err err:
                    OnError(new Exception(err.Reason));
                    return true;
                case ErrThenStop err:
                    OnErrorThenStop(new Exception(err.Reason));
                    return true;
                case Complete _:
                    OnComplete();
                    return true;
                case CompleteThenStop _:
                    OnCompleteThenStop();
                    return true;
                case Boom _:
                    throw new Exception("boom");
                case ThreadName _:
                    _probe.Tell(Context.Props.Dispatcher /*Thread.CurrentThread.Name*/); // TODO fix me when thread name is set by dispatcher
                    return true;
                default:
                    return false;
            }
        }
    }

    internal class TestPublisherWithStash : TestPublisher, IWithUnboundedStash
    {
        public TestPublisherWithStash(IActorRef probe) : base(probe)
        {
        }

        public new static Props Props(IActorRef probe, bool useTestDispatcher = true)
        {
            var p = Akka.Actor.Props.Create(() => new TestPublisherWithStash(probe));
            return useTestDispatcher ? p.WithDispatcher("akka.test.stream-dispatcher") : p;
        }

        protected override bool Receive(object message)
        {
            if ("unstash".Equals(message))
            {
                Stash.UnstashAll();
                Context.Become(base.Receive);
            }
            else
                Stash.Stash();

            return true;
        }
        
        public IStash Stash { get; set; }
    }

    internal class Sender : Actors.ActorPublisher<int>
    {
        public static Props Props { get; } = Props.Create<Sender>().WithDispatcher("akka.test.stream-dispatcher");

        private IImmutableList<int> _buffer = ImmutableList<int>.Empty;

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case int i:
                    if (_buffer.Count == 0 && TotalDemand > 0)
                        OnNext(i);
                    else
                    {
                        _buffer = _buffer.Add(i);
                        DeliverBuffer();
                    }
                    return true;
                case Request _:
                    DeliverBuffer();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        private void DeliverBuffer()
        {
            if (TotalDemand <= 0)
                return;

            if (TotalDemand <= int.MaxValue)
            {
                var use = _buffer.Take((int) TotalDemand).ToImmutableList();
                _buffer = _buffer.Skip((int) TotalDemand).ToImmutableList();

                use.ForEach(OnNext);
            }
            else
            {
                var use = _buffer.Take(int.MaxValue).ToImmutableList();
                _buffer = _buffer.Skip(int.MaxValue).ToImmutableList();

                use.ForEach(OnNext);
                DeliverBuffer();
            }
        }
    }

    internal class TimeoutingPublisher : Actors.ActorPublisher<int>
    {
        public static Props Props(IActorRef probe, TimeSpan timeout) =>
                Akka.Actor.Props.Create(() => new TimeoutingPublisher(probe, timeout))
                    .WithDispatcher("akka.test.stream-dispatcher");

        private readonly IActorRef _probe;

        public TimeoutingPublisher(IActorRef probe, TimeSpan timeout) 
        {
            _probe = probe;
            SubscriptionTimeout = timeout;
        }
        
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    OnNext(1);
                    return true;
                case SubscriptionTimeoutExceeded _:
                    _probe.Tell("timed-out");
                    Context.System.Scheduler.ScheduleTellOnce(SubscriptionTimeout, _probe, "cleaned-up", Self);
                    Context.System.Scheduler.ScheduleTellOnce(SubscriptionTimeout, Self, PoisonPill.Instance, Nobody.Instance);
                    return true;
                default:
                    return false;
            }
        }
    }

    internal class Receiver : ActorSubscriber
    {
        public static Props Props(IActorRef probe) =>
            Akka.Actor.Props.Create(() => new Receiver(probe)).WithDispatcher("akka.test.stream-dispatcher");

        private readonly IActorRef _probe;

        public Receiver(IActorRef probe)
        {
            _probe = probe;
        }

        public override IRequestStrategy RequestStrategy { get; } = new WatermarkRequestStrategy(10);

        protected override bool Receive(object message)
        {
            if (!(message is OnNext next)) 
                return false;
            
            _probe.Tell(next.Element);
            return true;
        }
    }

    internal class TotalDemand
    {
        public readonly long Elements;

        public TotalDemand(long elements)
        {
            Elements = elements;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((TotalDemand) obj);
        }

        protected bool Equals(TotalDemand other) => Elements == other.Elements;

        public override int GetHashCode() => Elements.GetHashCode();
    }

    internal class Produce
    {
        public readonly string Elem;

        public Produce(string elem)
        {
            Elem = elem;
        }
    }

    internal class Err
    {
        public readonly string Reason;

        public Err(string reason)
        {
            Reason = reason;
        }
    }

    internal class ErrThenStop
    {
        public readonly string Reason;

        public ErrThenStop(string reason)
        {
            Reason = reason;
        }
    }

    internal class Boom
    {
        public static Boom Instance { get; } = new Boom();

        private Boom() { }
    }

    internal class Complete
    {
        public static Complete Instance { get; } = new Complete();

        private Complete() { }
    }

    internal class CompleteThenStop
    {
        public static CompleteThenStop Instance { get; } = new CompleteThenStop();

        private CompleteThenStop() { }
    }

    internal class ThreadName
    {
        public static ThreadName Instance { get; } = new ThreadName();

        private ThreadName() { }
    }
}
