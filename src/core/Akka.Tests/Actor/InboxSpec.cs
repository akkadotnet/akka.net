//-----------------------------------------------------------------------
// <copyright file="InboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        public void Inbox_support_watch()
        {
            _inbox.Watch(TestActor);

            // check watch
            TestActor.Tell(PoisonPill.Instance);
            var received = _inbox.Receive(TimeSpan.FromSeconds(1));

            received.GetType().ShouldBe(typeof(Terminated));
            var terminated = (Terminated)received;
            terminated.ActorRef.ShouldBe(TestActor);
        }

        [Fact]
        public void Inbox_support_queueing_multiple_queries()
        {
            var tasks = new[]
                {
                    Task.Factory.StartNew(() => _inbox.Receive()),
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(100);
                        return _inbox.ReceiveWhere(x => x.ToString() == "world"); 
                    }), 
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(200);
                        return _inbox.ReceiveWhere(x => x.ToString() == "hello"); 
                    }) 
                };

            _inbox.Receiver.Tell(42);
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            Task.WaitAll(tasks.Cast<Task>().ToArray());

            tasks[0].Result.ShouldBe(42);
            tasks[1].Result.ShouldBe("world");
            tasks[2].Result.ShouldBe("hello");
        }

        [Fact]
        public void Inbox_support_selective_receives()
        {
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            var selection = _inbox.ReceiveWhere(x => x.ToString() == "world");       
            selection.ShouldBe("world");
            _inbox.Receive().ShouldBe("hello");
        }

        [Fact]
        public void Inbox_have_maximum_queue_size()
        {
            try
            {
                //Fill the inbox (it can hold 1000) messages
                foreach (var zero in Enumerable.Repeat(0, 1000))
                    _inbox.Receiver.Tell(zero);

                ExpectNoMsg(TimeSpan.FromSeconds(1));

                //The inbox is full. Sending another message should result in a Warning message
                EventFilter.Warning(start:"Dropping message").ExpectOne(() => _inbox.Receiver.Tell(42));

                //The inbox is still full. But since the warning message has already been sent, no more warnings should be sent
                _inbox.Receiver.Tell(42);
                ExpectNoMsg(TimeSpan.FromSeconds(1));

                //Receive all messages from the inbox
                var gotit = Enumerable.Repeat(0, 1000).Select(_ => _inbox.Receive());
                foreach (var o in gotit)
                {
                    o.ShouldBe(0);
                }

                //The inbox should be empty now, so receiving should result in a timeout
                Intercept<TimeoutException>(() =>
                {
                    var received = _inbox.Receive(TimeSpan.FromSeconds(1));
                    Log.Error("Received " + received);
                });
            }
            finally
            {
                Sys.EventStream.Unsubscribe(TestActor, typeof(Warning));
            }
        }

        [Fact]
        public void Inbox_have_a_default_and_custom_timeouts()
        {
            Within(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), () =>
            {
                Intercept<TimeoutException>(() => _inbox.Receive());
                return true;
            });

            Within(TimeSpan.FromSeconds(1), () =>
            {
                Intercept<TimeoutException>(() => _inbox.Receive(TimeSpan.FromMilliseconds(100)));
                return true;
            });
        }

        [Fact]
        public void Select_WithClient_should_update_Client_and_copy_the_rest_of_the_properties_BUG_427()
        {
            var deadline = new TimeSpan(Sys.Scheduler.MonotonicClock.Ticks/2); //Some point in the past
            Predicate<object> predicate = o => true;
            var actorRef = new EmptyLocalActorRef(((ActorSystemImpl)Sys).Provider, new RootActorPath(new Address("akka", "test")), Sys.EventStream);
            var select = new Select(deadline, predicate, actorRef);

            var updatedActorRef = new EmptyLocalActorRef(((ActorSystemImpl)Sys).Provider, new RootActorPath(new Address("akka2", "test2")), Sys.EventStream);

            var updatedSelect = (Select)select.WithClient(updatedActorRef);
            updatedSelect.Deadline.ShouldBe(deadline);
            updatedSelect.Predicate.ShouldBe(predicate);
            updatedSelect.Client.ShouldBe(updatedActorRef);
        }

        [Fact]
        public void Inbox_Receive_will_timeout_gracefully_if_timeout_is_already_expired()
        {
            var task = _inbox.ReceiveAsync(TimeSpan.FromSeconds(-1));

            Assert.True(task.Wait(1000), "Receive did not complete in time.");
            Assert.IsType<Status.Failure>(task.Result);
        }
    }
}

