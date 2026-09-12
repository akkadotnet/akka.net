//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;


namespace Akka.Tests.Actor
{
    public class Tick { }

    public class TransparentTick : INotInfluenceReceiveTimeout { }
    
    public class ReceiveTimeoutSpec : AkkaSpec
    {
        public class TimeoutActor : ActorBase
        {
            private readonly IActorRef _probe;

            public TimeoutActor(IActorRef probe)
                : this(probe, TimeSpan.FromMilliseconds(500))
            {
            }

            public TimeoutActor(IActorRef probe, TimeSpan? timeout)
            {
                _probe = probe;
                Context.SetReceiveTimeout(timeout.GetValueOrDefault());
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _probe.Tell("timeout");
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                if (message is TransparentTick)
                {
                    return true;
                }

                return false;
            }
        }
        
        public class AsyncTimeoutActor : ReceiveActor
        {
            public AsyncTimeoutActor(IActorRef probe)
                : this(probe, TimeSpan.FromMilliseconds(500))
            {
            }

            public AsyncTimeoutActor(IActorRef probe, TimeSpan? timeout)
            {
                var log = Context.GetLogger();

                Context.SetReceiveTimeout(timeout.GetValueOrDefault());

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable AK1003
                ReceiveAsync<ReceiveTimeout>(async _ =>
                {
                    log.Info($"Received {nameof(ReceiveTimeout)}");
                    probe.Tell("timeout");
                });

                ReceiveAsync<TransparentTick>(async _ =>
                {
                    log.Info($"Received {nameof(TransparentTick)}");
                });

                ReceiveAsync<Tick>(async _ =>
                {
                    log.Info($"Received {nameof(Tick)}");
                });
#pragma warning restore AK1003
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            }

        }

        public class TurnOffTimeoutActor : ActorBase
        {
            private readonly IActorRef _probe;
            private readonly AtomicCounter _counter;

            public TurnOffTimeoutActor(IActorRef probe, AtomicCounter counter)
            {
                _probe = probe;
                _counter = counter;
                Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _counter.IncrementAndGet();
                    _probe.Tell("timeout");
                    Context.SetReceiveTimeout(null);
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                return false;
            }
        }

        public class NoTimeoutActor : ActorBase
        {
            private readonly IActorRef _probe;

            public NoTimeoutActor(IActorRef probe)
            {
                _probe = probe;
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _probe.Tell("timeout");
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                return false;
            }
        }

        public ReceiveTimeoutSpec(ITestOutputHelper output): base(output)
        {
        }
        
        [Fact]
        public async Task An_actor_with_receive_timeout_must_get_timeout()
        {
            var probe = CreateTestProbe();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(probe.Ref, TimeSpan.FromMilliseconds(500))));

            await probe.ExpectMsgAsync("timeout", TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_reschedule_timeout_after_regular_receive()
        {
            var probe = CreateTestProbe();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(probe.Ref, TimeSpan.FromMilliseconds(500))));

            timeoutActor.Tell(new Tick());
            await probe.ExpectMsgAsync("timeout", TestKitSettings.DefaultTimeout);

            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_be_able_to_turn_off_timeout_if_desired()
        {
            var count = new AtomicCounter(0);

            var probe = CreateTestProbe();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TurnOffTimeoutActor(probe.Ref, count)));

            timeoutActor.Tell(new Tick());
            await probe.ExpectMsgAsync("timeout", TestKitSettings.DefaultTimeout);
            count.Current.ShouldBe(1);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_not_receive_timeout_message_when_not_specified()
        {
            var probe = CreateTestProbe();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new NoTimeoutActor(probe.Ref)));

            // No receive-timeout was ever set, so no "timeout" should arrive. This also stops
            // parking a pool worker for the duration of the wait: ExpectNoMsgAsync awaits instead
            // of blocking a thread for TestKitSettings.DefaultTimeout.
            await probe.ExpectNoMsgAsync(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_get_timeout_while_receiving_NotInfluenceReceiveTimeout_messages()
        {
            var probe = CreateTestProbe();
            var receiveTimeout = TimeSpan.FromSeconds(1);
            var slack = TimeSpan.FromSeconds(4);
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(probe.Ref, receiveTimeout)));

            var cancelable = Sys.Scheduler.Advanced.ScheduleRepeatedlyCancelable(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                () =>
                {
                    timeoutActor.Tell(new TransparentTick());
                    timeoutActor.Tell(new Identify(null));
                });

            await probe.ExpectMsgAsync("timeout", receiveTimeout + slack);
            cancelable.Cancel();
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_async_actor_with_receive_timeout_must_get_timeout_while_receiving_NotInfluenceReceiveTimeout_messages()
        {
            var probe = CreateTestProbe();
            var receiveTimeout = TimeSpan.FromSeconds(1);
            var slack = TimeSpan.FromSeconds(4);
            var timeoutActor = Sys.ActorOf(Props.Create(() => new AsyncTimeoutActor(probe.Ref, receiveTimeout)));

            var cancelable = Sys.Scheduler.Advanced.ScheduleRepeatedlyCancelable(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                () =>
                {
                    timeoutActor.Tell(new TransparentTick());
                    //timeoutActor.Tell(new Identify(null));
                });

            await probe.ExpectMsgAsync("timeout", receiveTimeout + slack);
            cancelable.Cancel();
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_get_timeout_while_receiving_only_NotInfluenceReceiveTimeout_messages()
        {
            var probe = CreateTestProbe();
            var receiveTimeout = TimeSpan.FromSeconds(1);

            // Slack for scheduler lateness and mailbox dispatch. ExpectMsgAsync runs every budget
            // through Dilated(), so this scales with akka.test.timefactor. TestLatch built with
            // "new TestLatch(...)" does not dilate, which is why the latch had to go.
            var slack = TimeSpan.FromSeconds(4);

            // The tick is a local self-tell that never leaves the process; it does not need the
            // same margin as a real scheduled receive-timeout cycle. 1s is still generous for a
            // sub-millisecond event and keeps this de-flake from hiding a genuinely stuck mailbox.
            var tickSlack = TimeSpan.FromSeconds(1);

            Action<IActorDsl> actor = d =>
            {
                d.OnPreStart = c => c.SetReceiveTimeout(receiveTimeout);
                d.Receive<ReceiveTimeout>((_, c) =>
                {
                    c.Self.Tell(new TransparentTick());
                    probe.Ref.Tell("timeout");
                });
                d.Receive<TransparentTick>((_, _) => probe.Ref.Tell("tick"));
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));

            try
            {
                // Phase 1: actor start plus one full receive-timeout period.
                await probe.ExpectMsgAsync("timeout", receiveTimeout + slack);

                // The INotInfluenceReceiveTimeout message must really reach the actor. This is the
                // message whose handling has to leave the pending timer alone.
                await probe.ExpectMsgAsync("tick", tickSlack);

                // Phase 2, budgeted from phase 1 rather than from the start of the test. This is the
                // assertion the test is named for: the TransparentTick neither cancelled nor
                // re-armed the receive-timeout timer.
                await probe.ExpectMsgAsync("timeout", receiveTimeout + slack);
            }
            finally
            {
                Sys.Stop(timeoutActor);
            }
        }

        [Fact]
        public async Task Issue469_An_actor_with_receive_timeout_must_cancel_receive_timeout_when_terminated()
        {
            //This test verifies that bug #469 "ReceiveTimeout isn't cancelled when actor terminates" has been fixed
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(probe.Ref, TimeSpan.FromMilliseconds(500))));

            //make sure TestActor gets a notification when timeoutActor terminates
            Watch(timeoutActor);

            // Wait for the first ReceiveTimeout message on a probe kept separate from TestActor,
            // so it cannot be confused with the DeadLetter check below.
            await probe.ExpectMsgAsync("timeout", TestKitSettings.DefaultTimeout);

            //Stop and wait for the actor to terminate
            Sys.Stop(timeoutActor);
            await ExpectTerminatedAsync(timeoutActor);

            //We should not get any messages now. If we get a message now, 
            //it's a DeadLetter with ReceiveTimeout, meaning the receivetimeout wasn't cancelled.
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_be_able_to_turn_on_timeout_in_NotInfluenceReceiveTimeout_message_handler()
        {
            var probe = CreateTestProbe();

            Action<IActorDsl> actor = d =>
            {
                d.Receive<TransparentTick>((_, c) => c.SetReceiveTimeout(500.Milliseconds()));
                d.Receive<ReceiveTimeout>((_, _) => probe.Ref.Tell("timeout"));
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));
            timeoutActor.Tell(new TransparentTick());

            await probe.ExpectMsgAsync("timeout", TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task An_actor_with_receive_timeout_must_be_able_to_turn_off_timeout_in_NotInfluenceReceiveTimeout_message_handler()
        {
            var probe = CreateTestProbe();

            Action<IActorDsl> actor = d =>
            {
                d.OnPreStart = c => c.SetReceiveTimeout(500.Milliseconds());
                d.Receive<TransparentTick>((_, c) => c.SetReceiveTimeout(null));
                d.Receive<ReceiveTimeout>((_, _) => probe.Ref.Tell("timeout"));
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));
            timeoutActor.Tell(new TransparentTick());

            // The timer was turned off before it could fire, so no "timeout" ever arrives.
            await probe.ExpectNoMsgAsync(1.Seconds());
            Sys.Stop(timeoutActor);
        }
    }
}

