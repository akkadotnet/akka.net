//-----------------------------------------------------------------------
// <copyright file="InboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Tests.Util;
using Xunit;

namespace Akka.Tests.Actor
{
    public class InboxSpec : AkkaSpec
    {
        private readonly Inbox _inbox;

        public InboxSpec()
            : base("akka.actor.inbox.inbox-size=1000")  //Default is 1000 but just to make sure these tests don't fail we set it
        {
            _inbox = Inbox.Create(Sys);
        }

        [Fact]
        public async Task Inbox_support_watch()
        {
            _inbox.Watch(TestActor);

            // check watch
            TestActor.Tell(PoisonPill.Instance);
            var received = await _inbox.ReceiveAsync(TimeSpan.FromSeconds(1));

            received.GetType().ShouldBe(typeof(Terminated));
            var terminated = (Terminated)received;
            terminated.ActorRef.ShouldBe(TestActor);
        }

        [Fact]
        public async Task Inbox_support_queueing_multiple_queries()
        {
            // Use ReceiveAsync instead of the synchronous Receive: Receive's AwaitResult blocks the
            // calling thread on the wall clock for the same duration the inbox actor uses for its
            // own scheduled deadline. When that deadline fires first, it faults the task with
            // Status.Failure(TimeoutException), and the blocking wall-clock wait then rethrows it
            // wrapped in AggregateException instead of letting the intended TimeoutException surface
            // directly.
            var task0 = _inbox.ReceiveAsync();
            var task1 = Task.Factory.StartNew(() =>
            {
                Thread.Sleep(100);
                return _inbox.ReceiveWhere(x => x.ToString() == "world");
            });
            var task2 = Task.Factory.StartNew(() =>
            {
                Thread.Sleep(200);
                return _inbox.ReceiveWhere(x => x.ToString() == "hello");
            });

            _inbox.Receiver.Tell(42);
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            await Task.WhenAll(task0, task1, task2);

            (await task0).ShouldBe(42);
            (await task1).ShouldBe("world");
            (await task2).ShouldBe("hello");
        }

        [Fact]
        public async Task Inbox_support_selective_receives()
        {
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            var selection = _inbox.ReceiveWhere(x => x.ToString() == "world");
            selection.ShouldBe("world");
            (await _inbox.ReceiveAsync()).ShouldBe("hello");
        }

        [Fact]
        public async Task Inbox_have_maximum_queue_size()
        {
            try
            {
                //Fill the inbox (it can hold 1000) messages
                foreach (var zero in Enumerable.Repeat(0, 1000))
                    _inbox.Receiver.Tell(zero);

                await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

                //The inbox is full. Sending another message should result in a Warning message
                await EventFilter.Warning(start: "Dropping message").ExpectOneAsync(() => { _inbox.Receiver.Tell(42); return Task.CompletedTask; });

                //The inbox is still full. But since the warning message has already been sent, no more warnings should be sent
                _inbox.Receiver.Tell(42);
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

                //Receive all messages from the inbox
                for (var i = 0; i < 1000; i++)
                {
                    var o = await _inbox.ReceiveAsync();
                    o.ShouldBe(0);
                }

                //The inbox should be empty now, so receiving should result in a timeout
                await Assert.ThrowsAsync<TimeoutException>(() => _inbox.ReceiveAsync(TimeSpan.FromSeconds(1)));
            }
            finally
            {
                Sys.EventStream.Unsubscribe(TestActor, typeof(Warning));
            }
        }

        [Fact]
        public async Task Inbox_have_a_default_and_custom_timeouts()
        {
            await WithinAsync(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), async () =>
            {
                await Assert.ThrowsAsync<TimeoutException>(() => _inbox.ReceiveAsync());
            });

            await WithinAsync(TimeSpan.FromSeconds(1), async () =>
            {
                await Assert.ThrowsAsync<TimeoutException>(() => _inbox.ReceiveAsync(TimeSpan.FromMilliseconds(100)));
            });
        }

        [Fact]
        public void Select_WithClient_should_update_Client_and_copy_the_rest_of_the_properties_BUG_427()
        {
            var deadline = new TimeSpan(Sys.Scheduler.MonotonicClock.Ticks / 2); //Some point in the past
            Predicate<object> predicate = _ => true;
            var actorRef = new EmptyLocalActorRef(((ActorSystemImpl)Sys).Provider, new RootActorPath(new Address("akka", "test")), Sys.EventStream);
            var select = new Select(deadline, predicate, actorRef);

            var updatedActorRef = new EmptyLocalActorRef(((ActorSystemImpl)Sys).Provider, new RootActorPath(new Address("akka2", "test2")), Sys.EventStream);

            var updatedSelect = (Select)select.WithClient(updatedActorRef);
            updatedSelect.Deadline.ShouldBe(deadline);
            updatedSelect.Predicate.ShouldBe(predicate);
            updatedSelect.Client.ShouldBe(updatedActorRef);
        }

        [Fact]
        public async Task Inbox_Receive_will_timeout_gracefully_if_timeout_is_already_expired()
        {
            var task = _inbox.ReceiveAsync(TimeSpan.FromSeconds(-1));
            await Assert.ThrowsAnyAsync<Exception>(() => task.AwaitWithTimeout(TimeSpan.FromMilliseconds(1000)));
        }
    }
}
